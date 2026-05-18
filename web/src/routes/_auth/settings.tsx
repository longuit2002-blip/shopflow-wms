import { createFileRoute } from '@tanstack/react-router';
import { Settings } from 'lucide-react';
import { ComingSoon } from '../../components/primitives/ComingSoon';
import { t, useLocale } from '../../hooks/useLocale';

export const Route = createFileRoute('/_auth/settings')({
  component: SettingsStub,
});

function SettingsStub() {
  useLocale();
  return (
    <ComingSoon
      icon={Settings}
      screen={t('Cài đặt', 'Settings')}
      targetLabel={t('Phase 3', 'Phase 3')}
      blurb={t(
        'Cài đặt tenant, compliance, sub-processor, audit retention.',
        'Tenant configuration, compliance, sub-processors, audit retention.',
      )}
    />
  );
}
