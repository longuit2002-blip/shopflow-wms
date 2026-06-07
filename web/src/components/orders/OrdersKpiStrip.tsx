/**
 * OrdersKpiStrip — Orders header. Thin wrapper over the shared <StatRow>
 * primitive (identical vocabulary to Inventory + Compliance). Values from
 * useOrderKpiQuery(); SignalR keeps the cache fresh with a 2s polling
 * fallback when the hub is down (R13). camelCase wire shape.
 */

import { StatItem, StatRow } from '../primitives/StatRow';
import { useOrderKpiQuery } from '../../hooks/useOrdersQuery';
import { t, useLocale } from '../../hooks/useLocale';
import { fmtNum } from '../../lib/format';

export function OrdersKpiStrip() {
  const { lang } = useLocale();
  const { data, isLoading, isError } = useOrderKpiQuery();
  const fmt = (v: number | null | undefined) => (v == null ? null : fmtNum(v, lang));

  return (
    <StatRow
      ariaLabel={t('Chỉ số đơn hàng', 'Orders KPIs')}
      footer={
        isError ? (
          <div
            role="alert"
            style={{
              flexBasis: '100%',
              marginTop: 'var(--s-3)',
              fontSize: 'var(--text-xs)',
              color: 'var(--bad-ink)',
            }}
          >
            {t('Không tải được KPI; sẽ thử lại.', 'Could not load KPIs; retrying.')}
          </div>
        ) : null
      }
    >
      <StatItem
        label={t('Đơn đang xử lý', 'Active orders')}
        value={fmt(data?.activeOrders)}
        kind="neutral"
        isStale={isLoading}
      />
      <StatItem
        label={t('Chờ soạn', 'Awaiting pick')}
        value={fmt(data?.awaitingPick)}
        kind="info"
        isStale={isLoading}
      />
      <StatItem
        label={t('Chờ giao', 'Awaiting ship')}
        value={fmt(data?.awaitingShip)}
        kind="warn"
        isStale={isLoading}
      />
      <StatItem
        label={t('Lỗi hôm nay', 'Failed today')}
        value={fmt(data?.failedToday)}
        kind="bad"
        isStale={isLoading}
      />
    </StatRow>
  );
}
