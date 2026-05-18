import { createFileRoute } from '@tanstack/react-router';
import { Truck } from 'lucide-react';
import { ComingSoon } from '../../components/primitives/ComingSoon';
import { t, useLocale } from '../../hooks/useLocale';

export const Route = createFileRoute('/_auth/inbound')({
  component: InboundStub,
});

function InboundStub() {
  useLocale();
  return (
    <ComingSoon
      icon={Truck}
      screen={t('Nhập hàng', 'Inbound')}
      targetLabel={t('Sprint 8', 'Sprint 8')}
      blurb={t(
        'Phiếu nhập (PO), reconciliation và put-away suggestions sẽ được wire ở Sprint 8.',
        'Purchase orders, reconciliation, and put-away suggestions wire in Sprint 8.',
      )}
    />
  );
}
