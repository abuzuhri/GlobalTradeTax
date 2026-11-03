from django.conf import settings
from django.contrib.sites.models import Site

from saleor.account.models import User


if not settings.DEBUG or settings.DATABASES['default']['NAME'] != 'saleor_sandbox':
    raise RuntimeError('Sandbox initialization requires the dedicated DEBUG database')

email = credentials['email']
password = credentials['password']
user, created = User.objects.get_or_create(email=email)
if created:
    user.is_staff = True
    user.is_superuser = True
    user.is_active = True
    user.is_confirmed = True
    user.set_password(password)
    user.save()
elif not user.is_staff or not user.is_superuser or not user.check_password(password):
    raise RuntimeError('Existing sandbox account differs; refusing to overwrite it')

print('Sandbox administrator ready; password was not logged')
Site.objects.filter(pk=settings.SITE_ID).update(domain='localhost:18080', name='GlobalTradeTax sandbox')
