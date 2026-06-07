/**
 * LedgerRow — Sprint-6 plan U10.
 *
 * Single row in the reservation ledger drawer table. Formats timestamp,
 * order / line reference, signed quantity, status pill, and running
 * balance.
 *
 * Schema note (Sprint-6 trade-off #6, PascalCase wire shape): the API
 * returns `Id, OrderId, OrderLineId, Status, Quantity, Timestamp,
 * RunningBalance`. The prototype-table's separate `channel` + `kind`
 * columns require cross-module joins (trade-off #3); Sprint-7 backfills
 * them.
 */

import type { SkuLedgerEntry } from '../../api/inventory';
import { Pill, type PillKind } from '../primitives/Pill';
import { useLocale, type LocaleCode } from '../../hooks/useLocale';
import { fmtDateTime, fmtKeyTruncated, fmtNum } from '../../lib/format';

const STATUS_KIND: Record<string, PillKind> = {
  Reserved: 'info',
  Confirmed: 'ok',
  Released: 'default',
  Adjusted: 'accent',
};

function statusKind(status: string): PillKind {
  return STATUS_KIND[status] ?? 'default';
}

function statusLabel(status: string, lang: LocaleCode): string {
  if (lang === 'en') return status;
  switch (status) {
    case 'Reserved':
      return 'Đã giữ';
    case 'Confirmed':
      return 'Đã xác nhận';
    case 'Released':
      return 'Đã hoàn trả';
    case 'Adjusted':
      return 'Đã điều chỉnh';
    default:
      return status;
  }
}

export interface LedgerRowProps {
  entry: SkuLedgerEntry;
}

export function LedgerRow({ entry }: LedgerRowProps) {
  const { lang } = useLocale();
  const formatted = fmtNum(Math.abs(entry.quantity), lang);
  const signed = entry.quantity > 0 ? `+${formatted}` : entry.quantity < 0 ? `-${formatted}` : '0';
  const orderRef =
    entry.orderId && entry.orderLineId
      ? `${fmtKeyTruncated(entry.orderId, 12)} · ${fmtKeyTruncated(entry.orderLineId, 8)}`
      : '—';

  return (
    <tr data-testid="ledger-row">
      <td className="tnum">{fmtDateTime(entry.timestamp)}</td>
      <td className="mono" title={`${entry.orderId} · ${entry.orderLineId}`}>
        {orderRef}
      </td>
      <td
        className="num"
        style={{
          textAlign: 'right',
          color: entry.quantity > 0 ? 'var(--success-500)' : 'var(--ink-1)',
        }}
      >
        {signed}
      </td>
      <td>
        <Pill kind={statusKind(entry.status)}>{statusLabel(entry.status, lang)}</Pill>
      </td>
      <td className="num" style={{ textAlign: 'right' }}>
        {fmtNum(entry.runningBalance, lang)}
      </td>
    </tr>
  );
}
