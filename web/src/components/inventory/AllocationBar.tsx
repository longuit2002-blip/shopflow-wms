/**
 * AllocationBar — Sprint-6 plan U10.
 *
 * Horizontal stacked bar showing per-channel inventory allocation share.
 * Each segment width = (Allocated / total Allocated) × 100 %. Channel
 * colour resolves to `--ch-<channel-lowercase>` from tokens.css §40
 * (Shopee orange, Lazada indigo, TikTok near-black, Shopify green, Sendo
 * red). Unknown channel names degrade to a neutral grey.
 *
 * Empty / zero-total state: renders a placeholder strip with the
 * "no allocation" label. Sprint-6 trade-off #3 (no cross-module joins)
 * means the wire shape ships `Allocations: []`; Sprint-7 backfills the
 * real per-channel split and this view begins to render the stacked bar.
 */

import type { ChannelAllocation } from '../../api/inventory';
import { t, useLocale } from '../../hooks/useLocale';
import { fmtNum } from '../../lib/format';

const CHANNEL_COLOR: Record<string, string> = {
  shopee: 'var(--ch-shopee)',
  lazada: 'var(--ch-lazada)',
  tiktok: 'var(--ch-tiktok)',
  shopify: 'var(--ch-shopify)',
  sendo: 'var(--ch-sendo)',
};

function channelColor(name: string): string {
  return CHANNEL_COLOR[name.toLowerCase()] ?? 'var(--neutral-400)';
}

export interface AllocationBarProps {
  allocations: ChannelAllocation[];
}

export function AllocationBar({ allocations }: AllocationBarProps) {
  const { lang } = useLocale();
  const total = allocations.reduce((sum, a) => sum + a.Allocated, 0);

  if (allocations.length === 0 || total <= 0) {
    return (
      <div
        data-testid="alloc-bar-empty"
        style={{
          padding: 'var(--s-3) var(--s-4)',
          borderRadius: 'var(--radius-md)',
          background: 'var(--neutral-50)',
          border: '1px solid var(--neutral-200)',
          color: 'var(--ink-2)',
          fontSize: 'var(--text-xs)',
          letterSpacing: '0.04em',
          textTransform: 'uppercase',
          fontWeight: 600,
        }}
      >
        {t('Chưa có phân bổ kênh', 'No channel allocation')}
      </div>
    );
  }

  return (
    <div data-testid="alloc-bar">
      <div
        role="img"
        aria-label={t('Phân bổ kênh', 'Channel allocation')}
        style={{
          display: 'flex',
          height: 12,
          borderRadius: 'var(--radius-md)',
          overflow: 'hidden',
          border: '1px solid var(--neutral-200)',
        }}
      >
        {allocations.map((a) => {
          const pct = (a.Allocated / total) * 100;
          return (
            <div
              key={a.Channel}
              data-channel={a.Channel}
              title={`${a.Channel} — ${fmtNum(a.Allocated, lang)}`}
              style={{
                width: `${pct}%`,
                background: channelColor(a.Channel),
              }}
            />
          );
        })}
      </div>
      <ul
        style={{
          display: 'flex',
          flexWrap: 'wrap',
          gap: 'var(--s-3)',
          margin: 'var(--s-2) 0 0 0',
          padding: 0,
          listStyle: 'none',
          fontSize: 'var(--text-xs)',
          color: 'var(--ink-2)',
        }}
      >
        {allocations.map((a) => {
          const pct = Math.round((a.Allocated / total) * 100);
          return (
            <li
              key={a.Channel}
              style={{
                display: 'inline-flex',
                alignItems: 'center',
                gap: 'var(--s-1)',
              }}
            >
              <span
                className="ch-dot"
                style={{ background: channelColor(a.Channel) }}
              />
              <span>{a.Channel}</span>
              <span className="tnum" style={{ color: 'var(--ink-1)', fontWeight: 600 }}>
                {pct}%
              </span>
            </li>
          );
        })}
      </ul>
    </div>
  );
}
