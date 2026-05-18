import { createFileRoute } from '@tanstack/react-router';
import { Receipt } from 'lucide-react';
import { ComingSoon } from '../../components/primitives/ComingSoon';
import { t, useLocale } from '../../hooks/useLocale';

export const Route = createFileRoute('/_auth/outbound')({
  component: OutboundStub,
});

function OutboundStub() {
  useLocale();
  return (
    <ComingSoon
      icon={Receipt}
      screen={t('Đơn hàng', 'Outbound')}
      targetLabel={t('Sprint 7', 'Sprint 7')}
      blurb={t(
        'Quản lý đơn hàng, pick → pack → ship saga + tracking sẽ landed ở Sprint 7.',
        'Order management, pick → pack → ship saga, and tracking land in Sprint 7.',
      )}
    />
  );
}
