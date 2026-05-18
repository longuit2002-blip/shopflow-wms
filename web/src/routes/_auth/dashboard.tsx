import { createFileRoute } from '@tanstack/react-router';
import { LayoutDashboard } from 'lucide-react';
import { ComingSoon } from '../../components/primitives/ComingSoon';
import { t, useLocale } from '../../hooks/useLocale';

export const Route = createFileRoute('/_auth/dashboard')({
  component: DashboardStub,
});

function DashboardStub() {
  useLocale();
  return (
    <ComingSoon
      icon={LayoutDashboard}
      screen={t('Tổng quan', 'Dashboard')}
      targetLabel={t('Sprint 7', 'Sprint 7')}
      blurb={t(
        'Bảng KPI vận hành + SLA breach + saga state stream sẽ mở khoá sau khi SignalR landed.',
        'Operational KPIs, SLA breach feed, and saga state stream unlock once SignalR lands.',
      )}
    />
  );
}
