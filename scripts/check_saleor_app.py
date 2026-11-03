import argparse
import http.client
import json
from pathlib import Path
import time
from urllib.parse import quote


def request(port, path, payload=None, token=None, method=None):
    connection = http.client.HTTPConnection('localhost', port, timeout=15)
    headers = {'Content-Type': 'application/json'}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    try:
        connection.request(method or ('POST' if payload is not None else 'GET'), path,
                           body=json.dumps(payload).encode() if payload is not None else None, headers=headers)
        response = connection.getresponse()
        body = response.read()
        return response.status, json.loads(body) if body else None
    finally:
        connection.close()


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--results', type=Path, required=True)
    parser.add_argument('--lifecycle', action='store_true', help='Exercise this isolated review App deactivation, deletion and reinstallation')
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[1]
    settings = dict(line.split('=', 1) for line in (root / 'dev/saleor/.env').read_text(encoding='utf-8-sig').splitlines() if '=' in line)
    token = None

    def graphql(query, variables=None):
        status, result = request(18080, '/graphql/', {'query': query, 'variables': variables or {}}, token)
        if status != 200 or result.get('errors'):
            raise RuntimeError('GraphQL failed: ' + json.dumps(result.get('errors', status)))
        return result['data']

    login = graphql('mutation Login($email:String!,$password:String!){ tokenCreate(email:$email,password:$password){token errors{field message}}}',
                    {'email': settings['SANDBOX_ADMIN_EMAIL'], 'password': settings['SANDBOX_ADMIN_PASSWORD']})['tokenCreate']
    if not login['token']:
        raise RuntimeError('Sandbox login failed')
    token = login['token']
    version = graphql('{shop{version}}')['shop']['version']
    if not version.startswith('3.23.45'):
        raise RuntimeError('Expected pinned Saleor sandbox version')
    schema = graphql('{__schema{mutationType{fields{name args{name type{kind name ofType{kind name ofType{kind name ofType{kind name}}}}}}}}}')
    mutations = {field['name']: field['args'] for field in schema['__schema']['mutationType']['fields']}

    def type_text(value):
        if value['kind'] == 'NON_NULL':
            return type_text(value['ofType']) + '!'
        if value['kind'] == 'LIST':
            return '[' + type_text(value['ofType']) + ']'
        return value['name']

    def mutate(name, values, selection):
        arguments = {argument['name']: argument['type'] for argument in mutations[name]}
        declarations = ','.join('$' + key + ':' + type_text(arguments[key]) for key in values)
        parameters = ','.join(key + ':$' + key for key in values)
        result = graphql('mutation(' + declarations + '){' + name + '(' + parameters + '){' + selection + ' errors{field message}}}', values)[name]
        if result.get('errors'):
            raise RuntimeError(name + ' failed: ' + json.dumps(result['errors']))
        return result

    apps = graphql('{apps(first:100){edges{node{id identifier isActive}}}}')['apps']['edges']
    installed = next((edge['node'] for edge in apps if edge['node']['identifier'] == 'globaltradetax.checkout-review' and edge['node']['isActive']), None)
    lifecycle = None
    historical = set()
    if args.lifecycle:
        if installed is None:
            installed = next((edge['node'] for edge in apps if edge['node']['identifier'] == 'globaltradetax.checkout-review'), None)
        if installed is None:
            raise RuntimeError('Lifecycle check requires the existing isolated review App')
        original_id = installed['id']
        if not installed['isActive']:
            mutate('appActivate', {'id': original_id}, 'app{id isActive}')
            time.sleep(2)
        status, initial = request(18081, '/saleor/sandbox/reviews', token=token)
        if status != 200:
            raise RuntimeError('Initial review read failed before lifecycle check')
        historical = {entry['review']['reference'] for entry in initial['reviews']}
        status, manifest = request(18081, '/saleor/sandbox/manifest')
        if status != 200 or len(manifest['webhooks']) != 2:
            raise RuntimeError('Updated lifecycle manifest is not running')
        current = graphql('query($id:ID!){app(id:$id){webhooks{id name}}}', {'id': original_id})['app']['webhooks']
        for hook in manifest['webhooks']:
            previous = next((item for item in current if item['name'] == hook['name']), None)
            values = {'name': hook['name'], 'targetUrl': hook['targetUrl'], 'isActive': True,
                      'asyncEvents': hook['asyncEvents'], 'query': hook['query']}
            if previous:
                mutate('webhookUpdate', {'id': previous['id'], 'input': values}, 'webhook{id}')
            else:
                mutate('webhookCreate', {'input': dict(values, app=original_id)}, 'webhook{id}')

        def wait_review_status(expected):
            deadline = time.monotonic() + 60
            while time.monotonic() < deadline:
                status, state = request(18081, '/saleor/sandbox/reviews', token=token)
                if status == expected:
                    return state
                time.sleep(1)
            raise RuntimeError('Lifecycle did not reach expected review status ' + str(expected))

        mutate('appDeactivate', {'id': original_id}, 'app{id isActive}')
        wait_review_status(403)
        print('PASS: real signed deactivation blocks review access', flush=True)
        mutate('appActivate', {'id': original_id}, 'app{id isActive}')
        wait_review_status(200)
        print('PASS: real signed activation restores review access', flush=True)
        mutate('appDelete', {'id': original_id}, 'app{id}')
        wait_review_status(403)
        print('PASS: real signed deletion revokes installation', flush=True)
        lifecycle = {'originalAppId': original_id, 'deactivationBlockedReads': True,
                     'reactivationRestoredReads': True, 'deletionBlockedReads': True}
        installed = None
    if installed is None:
        mutate('appInstall', {'input': {'appName': 'GlobalTradeTax Review',
               'manifestUrl': 'http://gtt.sandbox.test:8080/saleor/sandbox/manifest',
               'activateAfterInstallation': True, 'permissions': ['MANAGE_CHECKOUTS']}}, 'appInstallation{id}')
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline:
            apps = graphql('{apps(first:100){edges{node{id identifier isActive}}}}')['apps']['edges']
            installed = next((edge['node'] for edge in apps if edge['node']['identifier'] == 'globaltradetax.checkout-review' and edge['node']['isActive']), None)
            review_status, review_state = request(18081, '/saleor/sandbox/reviews', token=token)
            if installed and review_status == 200 and review_state.get('registered'):
                break
            installed = None
            time.sleep(1)
        if installed is None:
            raise RuntimeError('App installation did not complete; inspect the sandbox worker log')

    if lifecycle is not None:
        if installed['id'] == lifecycle['originalAppId']:
            raise AssertionError('Reinstallation did not create a new App identity')
        status, restored = request(18081, '/saleor/sandbox/reviews', token=token)
        if status != 200 or not historical.issubset({entry['review']['reference'] for entry in restored['reviews']}):
            raise AssertionError('Historical reviews were lost during reinstallation')
        lifecycle.update({'newAppId': installed['id'], 'reinstallationVerified': True,
                          'historicalReviewsPreserved': len(historical)})
        print('PASS: verified reinstallation preserves historical reviews', flush=True)

    channels = graphql('{channels{id slug}}')['channels']
    channel = next((value for value in channels if value['slug'] == 'gtt-uk-review'), None)
    if channel is None:
        channel = mutate('channelCreate', {'input': {'name': 'GlobalTradeTax UK Sandbox', 'slug': 'gtt-uk-review',
                         'currencyCode': 'GBP', 'defaultCountry': 'GB', 'isActive': True}}, 'channel{id slug}')['channel']
    variant = graphql('{productVariant(sku:"GTT-REVIEW-SHIRT"){id product{id}}}')['productVariant']
    if variant is None:
        types = graphql('{productTypes(first:100){edges{node{id name}}}}')['productTypes']['edges']
        product_type = next((edge['node'] for edge in types if edge['node']['name'] == 'GlobalTradeTax Sandbox'), None)
        if product_type is None:
            product_type = mutate('productTypeCreate', {'input': {'name': 'GlobalTradeTax Sandbox', 'hasVariants': True,
                                  'isShippingRequired': False}}, 'productType{id}')['productType']
        categories = graphql('{categories(first:100){edges{node{id slug}}}}')['categories']['edges']
        category = next((edge['node'] for edge in categories if edge['node']['slug'] == 'gtt-sandbox'), None)
        if category is None:
            category = mutate('categoryCreate', {'input': {'name': 'GlobalTradeTax Sandbox', 'slug': 'gtt-sandbox'}}, 'category{id}')['category']
        product = mutate('productCreate', {'input': {'name': 'GlobalTradeTax Review Shirt', 'slug': 'gtt-review-shirt',
                         'productType': product_type['id'], 'category': category['id']}}, 'product{id}')['product']
        mutate('updateMetadata', {'id': product['id'], 'input': [{'key': 'gtt.hsCode', 'value': '6109100000'},
               {'key': 'gtt.countryOfOrigin', 'value': 'CN'}]}, 'item{__typename}')
        mutate('productChannelListingUpdate', {'id': product['id'], 'input': {'updateChannels': [
               {'channelId': channel['id'], 'isPublished': True, 'visibleInListings': True, 'isAvailableForPurchase': True}]}}, 'product{id}')
        variant = mutate('productVariantCreate', {'input': {'product': product['id'], 'sku': 'GTT-REVIEW-SHIRT', 'attributes': [],
                         'trackInventory': False}}, 'productVariant{id}')['productVariant']
        mutate('productVariantChannelListingUpdate', {'id': variant['id'], 'input': [
               {'channelId': channel['id'], 'price': '12.50'}]}, 'variant{id}')

    checkout = mutate('checkoutCreate', {'input': {'channel': 'gtt-uk-review', 'email': 'sandbox.customer@example.test',
                      'lines': [{'variantId': variant['id'], 'quantity': 2}]}}, 'checkout{id lines{id quantity}}')['checkout']

    def wait_for_quantity(quantity):
        deadline = time.monotonic() + 60
        while time.monotonic() < deadline:
            status, result = request(18081, '/saleor/sandbox/reviews', token=token)
            if status == 200:
                matches = [entry for entry in result['reviews'] if entry['review']['sourceId'] == checkout['id']
                           and entry['review']['lines'][0]['quantity'] == quantity]
                if matches:
                    return matches[0]
            time.sleep(1)
        raise RuntimeError('No automatically delivered review for the real checkout; inspect the worker and receiver logs')

    created = wait_for_quantity(2)
    mutate('checkoutLinesUpdate', {'id': checkout['id'], 'lines': [{'variantId': variant['id'], 'quantity': 3}]}, 'checkout{id}')
    updated = wait_for_quantity(3)
    for entry in (created, updated):
        review = entry['review']
        if review['approvedForPayment'] or any(review[field] is not None for field in ('importDuty', 'importVat', 'clearanceFee', 'landedTotal')):
            raise AssertionError('Unexpected payable customs output')
        if review['lines'][0]['hsCode'] != '6109100000' or review['lines'][0]['countryOfOrigin'] != 'CN':
            raise AssertionError('Real product metadata was not preserved')
    if request(18081, '/saleor/sandbox/reviews')[0] != 401 or request(18081, '/saleor/sandbox/reviews', token='invalid')[0] != 401:
        raise AssertionError('Review screen data is not protected')
    summary = {'saleorVersion': version, 'appInstalled': True, 'appId': installed['id'], 'checkoutId': checkout['id'],
               'createdEvent': created['eventType'], 'updatedEvent': updated['eventType'],
               'createdQuantity': 2, 'updatedQuantity': 3, 'automaticWebhookDelivery': True,
               'signedReceiverAccepted': True, 'productMetadataPreserved': True,
               'anonymousReviewAccess': 401, 'invalidTokenReviewAccess': 401,
               'payableAmountsRemainNull': True, 'reviewUi': 'http://localhost:18081/saleor/sandbox/app',
               'dashboardApp': 'http://localhost:19090/extensions/app/' + quote(installed['id'], safe='')}
    if lifecycle is not None:
        summary['lifecycle'] = lifecycle
    args.results.mkdir(parents=True, exist_ok=True)
    (args.results / 'saleor-app-summary.json').write_text(json.dumps(summary, indent=2), encoding='utf-8')
    (args.results / 'saleor-real-checkout-reviews.json').write_text(json.dumps([created, updated], indent=2), encoding='utf-8')
    print(json.dumps(summary, indent=2))


if __name__ == '__main__':
    main()
