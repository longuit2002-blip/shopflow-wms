import { createFileRoute } from '@tanstack/react-router';
import { FileSearch } from 'lucide-react';
import { ComingSoon } from '../../components/primitives/ComingSoon';
import { t, useLocale } from '../../hooks/useLocale';

export const Route = createFileRoute('/_auth/audit')({
  component: AuditStub,
});

function AuditStub() {
  useLocale();
  return (
    <ComingSoon
      icon={FileSearch}
      screen={t('Audit log', 'Audit log')}
      targetLabel={t('Phase 3', 'Phase 3')}
      blurb={t(
        'Audit event stream với trace ID, idempotency key, full payload diff.',
        'Audit event stream with trace ID, idempotency key, and full payload diff.',
      )}
    />
  );
}
