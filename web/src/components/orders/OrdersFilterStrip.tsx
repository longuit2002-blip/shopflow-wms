/**
 * OrdersFilterStrip — Sprint-7 plan U10.
 *
 * Controlled filter strip for the Orders list. State is owned by the
 * route component (so the table query refetches when the filter changes);
 * this component is a presentational layer that emits onChange.
 *
 * Inputs (per plan):
 *   - status select  (All / Reserved / AwaitingPick / Picked /
 *     AwaitingPack / Packed / AwaitingShip / Shipped / Cancelled)
 *   - channel select (All / Shopee / Lazada / TikTok Shop / Direct)
 *   - since + until  (HTML date inputs — ISO yyyy-mm-dd, normalised to
 *     ISO 8601 UTC at the route layer)
 *   - search         (free-text, channelExternalOrderId substring)
 *
 * No URL-search-param persistence (Sprint-6 trade-off #4 carries forward).
 *
 * A11y: every input has a visible `<label>` associated via `htmlFor`. The
 * filter region is a `<form>` so screen readers announce the group. No
 * submit button — TanStack Query keys on `value` and refetches as soon as
 * `onChange` fires.
 */

import type { ReactNode } from 'react';
import { Search } from 'lucide-react';
import type { OrdersFilter } from '../../api/orders';
import { t, useLocale } from '../../hooks/useLocale';

/** Saga states the user can filter by. Wire-shape strings — keep verbatim. */
const STATUS_OPTIONS = [
  'Reserved',
  'AwaitingPick',
  'Picked',
  'AwaitingPack',
  'Packed',
  'AwaitingShip',
  'Shipped',
  'Cancelled',
] as const;

/** Channel-prefix label → wire-shape substring. */
const CHANNEL_OPTIONS: { label: string; value: string }[] = [
  { label: 'Shopee', value: 'SHOPEE' },
  { label: 'Lazada', value: 'LAZADA' },
  { label: 'TikTok Shop', value: 'TIKTOK' },
  { label: 'Direct', value: 'DIRECT' },
];

export interface OrdersFilterStripProps {
  value: OrdersFilter;
  onChange: (next: OrdersFilter) => void;
}

export function OrdersFilterStrip({ value, onChange }: OrdersFilterStripProps) {
  useLocale();

  function patch(partial: Partial<OrdersFilter>) {
    // Drop empty-string entries so the api/orders.ts query-builder treats
    // them as "absent" rather than `?status=`.
    const merged: OrdersFilter = { ...value, ...partial };
    (Object.keys(merged) as (keyof OrdersFilter)[]).forEach((k) => {
      const v = merged[k];
      if (v === '' || v == null) {
        delete merged[k];
      }
    });
    onChange(merged);
  }

  return (
    <form
      className="strip"
      role="search"
      aria-label={t('Bộ lọc đơn hàng', 'Order filters')}
      onSubmit={(e) => e.preventDefault()}
      style={{
        display: 'flex',
        alignItems: 'flex-end',
        gap: 'var(--s-3)',
        padding: 'var(--s-3) var(--s-6)',
        flexWrap: 'wrap',
      }}
    >
      <Field htmlFor="orders-status" label={t('Trạng thái', 'Status')}>
        <select
          id="orders-status"
          value={value.status ?? ''}
          onChange={(e) => patch({ status: e.target.value || undefined })}
          data-testid="orders-filter-status"
        >
          <option value="">{t('Tất cả', 'All')}</option>
          {STATUS_OPTIONS.map((s) => (
            <option key={s} value={s}>
              {s}
            </option>
          ))}
        </select>
      </Field>

      <Field htmlFor="orders-channel" label={t('Kênh bán', 'Channel')}>
        <select
          id="orders-channel"
          value={value.channel ?? ''}
          onChange={(e) => patch({ channel: e.target.value || undefined })}
          data-testid="orders-filter-channel"
        >
          <option value="">{t('Tất cả', 'All')}</option>
          {CHANNEL_OPTIONS.map((c) => (
            <option key={c.value} value={c.value}>
              {c.label}
            </option>
          ))}
        </select>
      </Field>

      <Field htmlFor="orders-since" label={t('Từ ngày', 'Since')}>
        <input
          id="orders-since"
          type="date"
          value={dateInputValue(value.since)}
          onChange={(e) => patch({ since: toIsoStart(e.target.value) })}
          data-testid="orders-filter-since"
        />
      </Field>

      <Field htmlFor="orders-until" label={t('Đến ngày', 'Until')}>
        <input
          id="orders-until"
          type="date"
          value={dateInputValue(value.until)}
          onChange={(e) => patch({ until: toIsoEnd(e.target.value) })}
          data-testid="orders-filter-until"
        />
      </Field>

      <Field htmlFor="orders-search" label={t('Tìm mã đơn', 'Search order id')}>
        <span
          style={{
            position: 'relative',
            display: 'inline-flex',
            alignItems: 'center',
          }}
        >
          <Search
            size={13}
            aria-hidden
            style={{ position: 'absolute', left: 8, color: 'var(--ink-3)' }}
          />
          <input
            id="orders-search"
            type="search"
            value={value.search ?? ''}
            onChange={(e) => patch({ search: e.target.value || undefined })}
            placeholder={t('SHOPEE_…', 'SHOPEE_…')}
            style={{ paddingLeft: 28, minWidth: 200 }}
            data-testid="orders-filter-search"
          />
        </span>
      </Field>
    </form>
  );
}

interface FieldProps {
  htmlFor: string;
  label: string;
  children: ReactNode;
}

function Field({ htmlFor, label, children }: FieldProps) {
  return (
    <label
      htmlFor={htmlFor}
      style={{
        display: 'inline-flex',
        flexDirection: 'column',
        gap: 'var(--s-1)',
        fontSize: 11,
        color: 'var(--ink-3)',
      }}
    >
      <span className="lbl">{label}</span>
      {children}
    </label>
  );
}

/**
 * The HTML `<input type="date">` value is always `yyyy-mm-dd` (or empty).
 * The API takes ISO 8601 UTC; the route layer can store the full ISO
 * string. Here we map between the two representations.
 */
function dateInputValue(iso?: string): string {
  if (!iso) return '';
  // Take the date part of an ISO 8601 timestamp, locally.
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '';
  const yyyy = d.getFullYear();
  const mm = String(d.getMonth() + 1).padStart(2, '0');
  const dd = String(d.getDate()).padStart(2, '0');
  return `${yyyy}-${mm}-${dd}`;
}

function toIsoStart(dateValue: string): string | undefined {
  if (!dateValue) return undefined;
  // yyyy-mm-dd → start-of-day UTC.
  const d = new Date(`${dateValue}T00:00:00.000Z`);
  return Number.isNaN(d.getTime()) ? undefined : d.toISOString();
}

function toIsoEnd(dateValue: string): string | undefined {
  if (!dateValue) return undefined;
  const d = new Date(`${dateValue}T23:59:59.999Z`);
  return Number.isNaN(d.getTime()) ? undefined : d.toISOString();
}
