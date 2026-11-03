# Saleor review App lifecycle and key rotation — TASK-012

Implemented on 2026-11-03 for the pinned Saleor 3.23.45 local sandbox. Customs collection and synchronous tax hooks remain disabled.

## Lifecycle contract

The 0.2.0 manifest adds a separate subscription for APP_INSTALLED, APP_UPDATED, APP_STATUS_CHANGED and APP_DELETED. It retains only MANAGE_CHECKOUTS. Saleor 3.23 supports delivering an App's own lifecycle events without granting it MANAGE_APPS: [official lifecycle explanation](https://saleor.io/blog/app-lifecycle-webhooks). The exact subscription fields were checked against [the pinned 3.23.45 subscription types](https://github.com/saleor/saleor/blob/3.23.45/saleor/graphql/webhook/subscription_types.py).

Every checkout/lifecycle subscription now selects `recipient { id }`. The receiver verifies the pinned signature and store header, rejects duplicate JSON properties, and requires the signed recipient to match the saved installation. Lifecycle payloads must also identify that same App and the expected App identifier. Type/header and timestamp validation happens before checkout deduplication.

- Deactivation blocks review reads and checkout writes with 403. The encrypted token is retained so a verified reactivation can resume the same installation.
- Activation is accepted only after the saved token proves the same active App with exactly MANAGE_CHECKOUTS against the fixed store API. Older lifecycle notifications cannot reactivate a suspended installation.
- Deletion marks a durable revoked binding and removes the token from the current encrypted state. Exact deletion retries are harmless. Later activation cannot resurrect a deleted installation.
- Registration can replace a revoked binding only with a different App ID whose candidate token has independently passed the existing fixed-store identity, active-status and permission checks. Historical reviews remain attached to the tenant/store; they are not financial quotes.
- Old-installation checkout and lifecycle deliveries cannot mutate the new binding. An atomic App/token check under the store lock also rejects a checkout write racing with deletion or replacement.
- Each review read and checkout delivery additionally checks the saved App token against Saleor. Deactivation, deletion, token revocation or permission loss fails closed even if a lifecycle notification is delayed. Transport/identity failure returns 503; it does not erase a binding based on an outage.

The existing single-instance file store and its tenant/store authenticated encryption remain in use. This adds lifecycle protection, not distributed transaction semantics.

## Upgrade an existing installation

Existing installations retain the old manifest subscriptions until explicitly updated. The sandbox verification helper aligns the existing checkout hook with the new recipient selection and adds the lifecycle hook using the sandbox administrator before running the lifecycle test. A newly installed 0.2.0 App receives both hooks from its manifest.

Do not merely deploy the receiver and assume old subscriptions changed automatically. Missing recipients are rejected. If a deletion notification was permanently lost, local live-token checks still block access, but automatic replacement remains blocked until an operator reconciles the binding. No unauthenticated reset or token-replacement endpoint was added.

## Signing-key rotation

`SaleorApp:Stores:{index}:VerificationKeys` optionally supplies one to three distinct operator-approved public RSA pins. When this array is configured, it replaces the legacy KeyId/PublicKeyPem pair completely; removing a pin really removes its trust. Unknown key IDs, incorrect signatures and private-key material fail verification. No request-controlled key discovery or JWKS fetch was added.

1. Independently verify the next Saleor public key and key ID through the operator's trusted deployment process.
2. Configure both old and next public pins; restart the review App and verify both signatures through tests/staging.
3. Switch the Saleor signer and allow the documented webhook retry window to drain.
4. Remove the old pin and verify old signatures are rejected. Removing a pin does not cancel an already verified in-flight request; drain/restart instances when immediate retirement is required.

Legacy single-pin configuration remains supported when VerificationKeys is absent. The manual merchant-key-protected review endpoint keeps its own existing pin configuration.

## Encryption-key rotation

The current `SaleorApp:EncryptionKey` remains the primary 32-byte hex key. Up to two distinct previous keys can be supplied in `SaleorApp:PreviousEncryptionKeys`. Existing envelope version 1 and existing binding files remain readable.

Under the existing store lock, a read tries the primary key and then the bounded previous-key set. A successful old-key read atomically rewrites that binding using the primary key and a fresh nonce before returning it. Tampered state or an unknown key fails authentication and is never rewritten. Failed rewrapping fails the operation rather than claiming successful migration.

1. Stop/drain all writers; back up the encrypted state and the original key separately, with access controls.
2. Provision a new random primary key through the secret configuration. Put the old primary in PreviousEncryptionKeys. Deploy all instances consistently; this implementation supports a single writer instance.
3. Access each configured binding through an authorized review request or normal signed event to migrate it. A reviewed inactive/revoked binding is also rewrapped before access is denied. Inventory every binding, including disabled/orphaned stores; lazy migration is not an automatic sweep.
4. Verify each binding can be read with the new key alone in an isolated restore test. Retire the old live key only after this evidence exists and account for backup retention/recovery policy.

Use environment variables or an untracked secret provider, for example `SaleorApp__EncryptionKey` and `SaleorApp__PreviousEncryptionKeys__0`. Never place real encryption keys in tracked samples, reports or command-line arguments. The tracked [configuration shape](../samples/saleor-app-rotation.settings.example.json) contains placeholders only. The sandbox's actual signing/encryption keys were not rotated by this verification; cryptographic overlap/retirement and state migration are exercised by automated tests.

## Local verification

- Release build with warnings as errors: passed with zero warnings/errors.
- Full xUnit suite: 143 passed, including 19 new lifecycle/rotation cases.
- Python suite: 25 passed; 10 general container HTTP smoke assertions passed; npm audit reported zero vulnerabilities.
- The real sandbox App was deactivated, reactivated, deleted and reinstalled through Saleor GraphQL. Signed lifecycle delivery produced 403 on deactivation/deletion and restored access after activation. The new installation changed from App ID `QXBwOjE=` to `QXBwOjI=` and retained all four original review references.
- New real checkout-created/updated notifications reached the replacement installation, with quantities 2 then 3. All monetary customs outputs stayed null and approvedForPayment stayed false; anonymous/invalid-token reads remained 401.
- Actual staff login and AppBridge authentication displayed six review cards inside the replacement Dashboard App. Its current local route is http://localhost:19090/extensions/app/QXBwOjI%3D.
- Generated UI bundle/notices reproduced exactly; workflow guard, actionlint, repository integrity and limited secret-pattern scans passed. No hosted CI or production deployment was performed.

The first real deactivation test exposed that the worker lacked the shared media volume. Immediate lifecycle payloads are written by the API while deferred checkout payloads had previously been generated inside the worker. Sharing the existing `saleor-media:/app/media` volume between API and worker fixes the missing-payload-file failure without deleting data.

Reproduce the isolated lifecycle/cart check with:

```powershell
python scripts/check_saleor_app.py --results C:\path\to\results --lifecycle
```

This intentionally deactivates/reactivates and deletes/reinstalls only the dedicated local review App. It keeps the store's product/cart data and historical review snapshots, then creates another review-only test checkout. Do not run it against a production store.

## Remaining work

Hosted CI and history scanning, operational retry/failure metrics and alerts, immediate revocation coordination across instances, operator recovery/provisioning UI, production TLS/backup/restore exercises, distributed persistence, and verified customs calculation remain open. TASK-012 stays in-progress.
