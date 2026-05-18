import { createFileRoute } from '@tanstack/react-router';
import { Building2 } from 'lucide-react';
import { ComingSoon } from '../../components/primitives/ComingSoon';
import { t, useLocale } from '../../hooks/useLocale';

export const Route = createFileRoute('/_auth/tenants')({
  component: TenantsStub,
});

function TenantsStub() {
  useLocale();
  return (
    <ComingSoon
      icon={Building2}
      screen={t('Tenants', 'Tenants')}
      targetLabel={t('Phase 3', 'Phase 3')}
      blurb={t(
        'Quản trị tenants — provisioning, archive, restore, scale gate dashboard.',
        'Tenants administration — provisioning, archive, restore, scale-gate dashboard.',
      )}
    />
  );
}
