import { createFileRoute } from '@tanstack/react-router';
import { RefreshCw } from 'lucide-react';
import { ComingSoon } from '../../components/primitives/ComingSoon';
import { t, useLocale } from '../../hooks/useLocale';

export const Route = createFileRoute('/_auth/sync')({
  component: SyncStub,
});

function SyncStub() {
  useLocale();
  return (
    <ComingSoon
      icon={RefreshCw}
      screen={t('Đồng bộ tồn', 'Stock sync')}
      targetLabel={t('Sprint 8', 'Sprint 8')}
      blurb={t(
        'Push log per-channel, breaker state, fairness floor + retry visibility.',
        'Per-channel push log, breaker state, fairness floor, and retry visibility.',
      )}
    />
  );
}
