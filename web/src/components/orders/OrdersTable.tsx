/**
 * OrdersTable — Sprint-7 plan U10.
 *
 * Renders the live order list. Click a row's first-cell button → navigates
 * to the order detail route at `/orders/$orderId` (U13 ships the detail
 * page; the navigation contract is stable now).
 *
 * Columns:
 *   External Order ID | Channel | Lines | Status | Age | Last transition
 *
 * KTD11 (Sprint-6 carry-over): the SKU/order-id cell hosts a real
 * `<button>` that owns the navigation click; the row itself is NOT a
 * button (axe `nested-interactive` would fire if anything else in the row
 * were interactive — even though Sprint-7 doesn't have a row-inline-edit
 * yet, we preserve the contract so Sprint-8 expansions don't regress).
 *
 * Wire shape: camelCase fields (Sprint-7.5 U1/U2 wire normalisation).
 * `OrderListItemDto.age` is the server-side .NET TimeSpan stringified by
 * System.Text.Json (e.g. "00:15:42" or "01:23:45.6789012"); we parse it
 * locally so the table can render "15 min ago" / "1h 23m ago".
 *
 * Status pill mapping (Pill kinds: 'ok', 'warn', 'bad', 'info', 'accent',
 * 'default' — see `components/primitives/Pill.tsx`):
 *   Shipped                         → ok       (success)
 *   Reserved / AwaitingPick /
 *     Picked / AwaitingPack /
 *     Packed / AwaitingShip         → info
 *   CompensatingReservation         → warn
 *   Cancelled                       → bad      (danger)
 *   Created / AwaitingReservation
 *     / null                        → default  (neutral)
 *
 * Last-transition timestamp uses `Intl.RelativeTimeFormat` so locale flips
 * regenerate phrasing without bespoke string tables.
 */

import { useMemo } from 'react';
import type { OrderListItemDto, OrdersFilter } from '../../api/orders';
import { useOrdersListQuery } from '../../hooks/useOrdersQuery';
import { Pill, type PillKind } from '../primitives/Pill';
import { useNavigate } from '@tanstack/react-router';
import { t, useLocale, type LocaleCode } from '../../hooks/useLocale';
import { fmtNum } from '../../lib/format';

export interface OrdersTableProps {
  filter: OrdersFilter;
  /**
   * Current 1-based page number (Sprint-7.5 U7). When provided alongside
   * `onPageChange`, the table renders a pagination footer reading from the
   * URL state. Omit to keep the Sprint-7 behaviour (no footer; single page).
   */
  page?: number;
  /** Page size (rows per page) used by the pagination footer math. */
  pageSize?: number;
  /** Pagination-footer callback (writes the new page to URL via the route). */
  onPageChange?: (page: number) => void;
}

const STATUS_KIND: Record<string, PillKind> = {
  Shipped: 'ok',
  Reserved: 'info',
  AwaitingPick: 'info',
  Picked: 'info',
  AwaitingPack: 'info',
  Packed: 'info',
  AwaitingShip: 'info',
  CompensatingReservation: 'warn',
  Cancelled: 'bad',
  Created: 'default',
  AwaitingReservation: 'default',
};

function statusKind(status: string | null): PillKind {
  if (!status) return 'default';
  return STATUS_KIND[status] ?? 'default';
}

export function OrdersTable({
  filter,
  page,
  pageSize,
  onPageChange,
}: OrdersTableProps) {
  const { lang } = useLocale();
  const { data, isLoading, isError } = useOrdersListQuery(filter);

  // Memo the rows so consumer re-renders that don't change `data` skip the
  // copy loop. The list cap from the API is `take=50` server-side anyway.
  const rows = useMemo(() => data?.items ?? [], [data]);

  if (isError) {
    return <ErrorState />;
  }

  if (rows.length === 0 && !isLoading) {
    return <EmptyState />;
  }

  const showPagination = onPageChange != null && page != null && pageSize != null;
  const totalCount = data?.totalCount ?? 0;
  const totalPages = pageSize ? Math.max(1, Math.ceil(totalCount / pageSize)) : 1;

  return (
    <div
      style={{
        overflow: 'auto',
        flex: 1,
        display: 'flex',
        flexDirection: 'column',
      }}
    >
      <table className="t-data">
        <thead>
          <tr>
            <th scope="col">{t('Mã đơn', 'External order ID')}</th>
            <th scope="col">{t('Kênh', 'Channel')}</th>
            <th scope="col" style={{ textAlign: 'right' }}>
              {t('Dòng', 'Lines')}
            </th>
            <th scope="col">{t('Trạng thái', 'Status')}</th>
            <th scope="col">{t('Tuổi đơn', 'Age')}</th>
            <th scope="col">{t('Chuyển gần nhất', 'Last transition')}</th>
          </tr>
        </thead>
        <tbody>
          {isLoading && rows.length === 0
            ? Array.from({ length: 5 }).map((_, i) => <SkeletonRow key={i} />)
            : rows.map((row) => (
                <OrderRow key={row.id} row={row} lang={lang} />
              ))}
        </tbody>
      </table>
      {showPagination && (
        <OrdersPaginationFooter
          page={page!}
          totalPages={totalPages}
          onPageChange={onPageChange!}
        />
      )}
    </div>
  );
}

interface OrdersPaginationFooterProps {
  page: number;
  totalPages: number;
  onPageChange: (page: number) => void;
}

function OrdersPaginationFooter({
  page,
  totalPages,
  onPageChange,
}: OrdersPaginationFooterProps) {
  const canPrev = page > 1;
  const canNext = page < totalPages;
  return (
    <nav
      aria-label={t('Phân trang đơn hàng', 'Orders pagination')}
      data-testid="orders-pagination"
      style={{
        display: 'flex',
        gap: 'var(--s-2)',
        justifyContent: 'flex-end',
        padding: 'var(--s-3) var(--s-6)',
        borderTop: '1px solid var(--line)',
      }}
    >
      <button
        type="button"
        className="btn"
        disabled={!canPrev}
        onClick={() => onPageChange(page - 1)}
        data-testid="orders-pagination-prev"
      >
        {t('Trước', 'Prev')}
      </button>
      <span className="t-sm" style={{ alignSelf: 'center', color: 'var(--ink-2)' }}>
        {t(`Trang ${page} / ${totalPages}`, `Page ${page} / ${totalPages}`)}
      </span>
      <button
        type="button"
        className="btn"
        disabled={!canNext}
        onClick={() => onPageChange(page + 1)}
        data-testid="orders-pagination-next"
      >
        {t('Tiếp', 'Next')}
      </button>
    </nav>
  );
}

interface OrderRowProps {
  row: OrderListItemDto;
  lang: LocaleCode;
}

function OrderRow({ row, lang }: OrderRowProps) {
  const navigate = useNavigate();
  const age = useMemo(() => formatAge(row.age, lang), [row.age, lang]);
  const lastTransition = useMemo(
    () => formatRelativeIso(row.lastTransitionAt, lang),
    [row.lastTransitionAt, lang],
  );

  function openDetail() {
    void navigate({
      to: '/orders/$orderId',
      params: { orderId: row.id },
    });
  }

  return (
    <tr
      className="orders-row"
      onClick={openDetail}
      style={{ cursor: 'pointer' }}
      data-testid={`orders-row-${row.id}`}
    >
      {/*
        Mouse-anywhere-in-row → detail navigation is a convenience; the
        semantic focus target is the order-id button in the first cell.
        Row itself is NOT role=button (KTD11 — see file header).
      */}
      <td>
        <button
          type="button"
          onClick={(e) => {
            e.stopPropagation();
            openDetail();
          }}
          aria-label={`${t('Mở chi tiết đơn', 'Open order detail for')} ${row.channelExternalOrderId}`}
          className="row-link"
          data-testid={`order-row-${row.id}`}
          style={{
            all: 'unset',
            cursor: 'pointer',
            fontFamily: 'var(--font-mono)',
            color: 'inherit',
          }}
        >
          {row.channelExternalOrderId}
        </button>
      </td>
      <td>{row.channel}</td>
      <td className="num" style={{ textAlign: 'right' }}>
        {fmtNum(row.lineCount, lang)}
      </td>
      <td>
        <Pill kind={statusKind(row.currentSagaState)}>
          {row.currentSagaState ?? t('Chưa khởi tạo', 'Pending')}
        </Pill>
      </td>
      <td className="tnum" style={{ whiteSpace: 'nowrap' }}>
        {age}
      </td>
      <td className="tnum" style={{ whiteSpace: 'nowrap' }}>
        {lastTransition}
      </td>
    </tr>
  );
}

function SkeletonRow() {
  return (
    <tr aria-hidden="true">
      {Array.from({ length: 6 }).map((_, i) => (
        <td key={i}>
          <div
            className="skeleton"
            style={{
              height: 12,
              borderRadius: 4,
              background: 'var(--bg-soft)',
              opacity: 0.6,
            }}
          />
        </td>
      ))}
    </tr>
  );
}

function EmptyState() {
  return (
    <div
      data-testid="orders-empty"
      style={{
        flex: 1,
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 'var(--s-8)',
        color: 'var(--ink-3)',
      }}
    >
      <div className="t-lg" style={{ fontWeight: 600, color: 'var(--ink-2)' }}>
        {t('Chưa có đơn hàng nào', 'No orders yet')}
      </div>
      <div className="t-sm" style={{ marginTop: 'var(--s-2)' }}>
        {t(
          'Bộ lọc hiện tại không trả về kết quả, hoặc chưa có đơn nào trong hệ thống.',
          'Either no orders match the current filter, or the system has no orders yet.',
        )}
      </div>
    </div>
  );
}

function ErrorState() {
  return (
    <div
      role="alert"
      style={{
        flex: 1,
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 'var(--s-8)',
        color: 'var(--bad-ink)',
      }}
    >
      <div className="t-lg" style={{ fontWeight: 600 }}>
        {t('Không tải được danh sách đơn', 'Could not load orders')}
      </div>
      <div className="t-sm" style={{ marginTop: 'var(--s-2)', color: 'var(--ink-2)' }}>
        {t(
          'Backend sẽ thử lại tự động sau vài giây.',
          'The backend will retry automatically in a few seconds.',
        )}
      </div>
    </div>
  );
}

// ── Time-format helpers ───────────────────────────────────────────────────

/**
 * Parse a .NET TimeSpan string emitted by System.Text.Json. The default
 * shape is `[d.]hh:mm:ss[.fffffff]`; we accept both. Returns milliseconds.
 * Returns null when the input is malformed (so the cell can render "—").
 */
function parseTimeSpanMs(input: string): number | null {
  // Strip optional fractional seconds.
  const m = /^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?$/.exec(input);
  if (!m) return null;
  const days = m[1] ? Number(m[1]) : 0;
  const hours = Number(m[2]);
  const minutes = Number(m[3]);
  const seconds = Number(m[4]);
  // .NET TimeSpan fractional is in 100-ns ticks at up to 7 digits; we only
  // need millisecond precision for display so we slice the first 3 chars.
  const fracMs = m[5] ? Number(m[5].slice(0, 3).padEnd(3, '0')) : 0;
  return ((days * 24 + hours) * 60 + minutes) * 60_000 + seconds * 1000 + fracMs;
}

/**
 * Render the parsed TimeSpan as "15 phút trước" / "1h 23m" / "2d 4h".
 * Mirrors TransitionsLog's `formatElapsed` philosophy without coupling at
 * the component-pair level; small enough to duplicate.
 */
function formatAge(ageString: string, lang: LocaleCode): string {
  const ms = parseTimeSpanMs(ageString);
  if (ms == null) return '—';

  const seconds = Math.floor(ms / 1000);
  if (seconds < 60) {
    return lang === 'en' ? `${seconds}s` : `${seconds} giây`;
  }

  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) {
    return lang === 'en' ? `${minutes} min` : `${minutes} phút`;
  }

  const hours = Math.floor(minutes / 60);
  const restMin = minutes % 60;
  if (hours < 24) {
    if (restMin === 0) return lang === 'en' ? `${hours}h` : `${hours} giờ`;
    return lang === 'en' ? `${hours}h ${restMin}m` : `${hours} giờ ${restMin} phút`;
  }

  const days = Math.floor(hours / 24);
  const restHours = hours % 24;
  if (restHours === 0) return lang === 'en' ? `${days}d` : `${days} ngày`;
  return lang === 'en' ? `${days}d ${restHours}h` : `${days} ngày ${restHours} giờ`;
}

/**
 * Render an ISO 8601 UTC timestamp as a locale-aware relative time via
 * `Intl.RelativeTimeFormat`. Returns "—" when the input is null.
 */
function formatRelativeIso(iso: string | null, lang: LocaleCode): string {
  if (!iso) return '—';
  const then = Date.parse(iso);
  if (!Number.isFinite(then)) return '—';
  const deltaMs = then - Date.now(); // negative for past
  const rtf = new Intl.RelativeTimeFormat(lang === 'en' ? 'en-US' : 'vi-VN', {
    numeric: 'auto',
  });

  const absSec = Math.abs(deltaMs) / 1000;
  if (absSec < 60) return rtf.format(Math.round(deltaMs / 1000), 'second');
  if (absSec < 3600) return rtf.format(Math.round(deltaMs / 60_000), 'minute');
  if (absSec < 86_400) return rtf.format(Math.round(deltaMs / 3_600_000), 'hour');
  return rtf.format(Math.round(deltaMs / 86_400_000), 'day');
}
