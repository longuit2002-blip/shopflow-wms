import { createFileRoute } from '@tanstack/react-router';
import { UserPlus } from 'lucide-react';
import { ComingSoon } from '../../components/primitives/ComingSoon';
import { t, useLocale } from '../../hooks/useLocale';

export const Route = createFileRoute('/_auth/onboarding')({
  component: OnboardingStub,
});

function OnboardingStub() {
  useLocale();
  return (
    <ComingSoon
      icon={UserPlus}
      screen={t('Khởi tạo mới', 'Onboard new')}
      targetLabel={t('Phase 3', 'Phase 3')}
      blurb={t(
        '4-step wizard cho tenant mới: legal info, channels, users, billing.',
        'Four-step wizard for new tenants: legal info, channels, users, billing.',
      )}
    />
  );
}
