/**
 * KPI stat row — Inventory header. Thin wrapper over the shared <StatRow>
 * primitive so the metric vocabulary is identical to Orders and Compliance.
 * Loading holds the prior value at reduced opacity across the 2s poll.
 */

import { StatItem, StatRow } from '../primitives/StatRow';
import { useInventorySummaryQuery } from '../../hooks/useInventoryQuery';
import { t, useLocale } from '../../hooks/useLocale';
import { fmtNum } from '../../lib/format';

export function KpiStrip() {
  const { lang } = useLocale();
  const { data, isLoading, isError } = useInventorySummaryQuery();
  const fmt = (v: number | null | undefined) => (v == null ? null : fmtNum(v, lang));

  return (
    <StatRow
      ariaLabel={t('Chỉ số tồn kho', 'Inventory KPIs')}
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
        label={t('Tồn thực', 'On hand')}
        value={fmt(data?.totalAvailable)}
        kind="neutral"
        isStale={isLoading}
      />
      <StatItem
        label={t('Đã giữ chỗ', 'Reserved')}
        value={fmt(data?.totalReserved)}
        kind="info"
        isStale={isLoading}
      />
      <StatItem
        label={t('Dưới mức an toàn', 'Below threshold')}
        value={fmt(data?.belowThresholdCount)}
        kind="warn"
        isStale={isLoading}
      />
      <StatItem
        label={t('Nguy cơ bán vượt', 'Oversell risk')}
        value={fmt(data?.oversellRiskCount)}
        kind="bad"
        isStale={isLoading}
      />
    </StatRow>
  );
}
