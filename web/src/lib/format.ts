/**
 * Locale-aware formatters ported from the design canon `data.jsx`
 * (~line 210). STYLING_SPECS §5 — Vietnamese number conventions:
 *
 *   - VND: "1.234.567 ₫" (Intl vi-VN thousands `.`, suffix NBSP + ₫).
 *   - Dates short: dd/mm/yyyy.
 *   - Times: 24-h, colon-separated.
 *   - Relative time (vi): vừa xong / N giây trước / N phút trước /
 *     N giờ trước → falls back to absolute at >24h.
 *
 * The English path falls back to en-US Intl + a parallel `fmtAgeEn`
 * (data.jsx ships vi-only; STYLING_SPECS flags the gap).
 */

import type { LocaleCode } from '../hooks/useLocale';

const NBSP = ' ';

const VND_FMT = new Intl.NumberFormat('vi-VN');
const USD_FMT = new Intl.NumberFormat('en-US', { style: 'currency', currency: 'USD' });
const VI_NUM = new Intl.NumberFormat('vi-VN');
const EN_NUM = new Intl.NumberFormat('en-US');

export function fmtVND(value: number): string {
  return `${VND_FMT.format(Math.round(value))}${NBSP}₫`;
}

export function fmtUSD(value: number): string {
  return USD_FMT.format(value);
}

export function fmtNum(value: number, lang: LocaleCode = 'vi'): string {
  return (lang === 'en' ? EN_NUM : VI_NUM).format(value);
}

export function fmtDate(date: Date | string): string {
  const d = typeof date === 'string' ? new Date(date) : date;
  const dd = String(d.getDate()).padStart(2, '0');
  const mm = String(d.getMonth() + 1).padStart(2, '0');
  return `${dd}/${mm}/${d.getFullYear()}`;
}

export function fmtTime(date: Date | string): string {
  const d = typeof date === 'string' ? new Date(date) : date;
  const hh = String(d.getHours()).padStart(2, '0');
  const mm = String(d.getMinutes()).padStart(2, '0');
  return `${hh}:${mm}`;
}

export function fmtDateTime(date: Date | string): string {
  return `${fmtDate(date)}${NBSP}·${NBSP}${fmtTime(date)}`;
}

/**
 * Relative-time helper. Returns "vừa xong" / "N giây trước" /
 * "N phút trước" / "N giờ trước" for vi; en parallel.
 * Falls back to absolute dd/mm/yyyy at >24h.
 */
export function fmtAge(
  date: Date | string,
  lang: LocaleCode = 'vi',
  now: Date = new Date(),
): string {
  const d = typeof date === 'string' ? new Date(date) : date;
  const deltaSec = Math.max(0, Math.floor((now.getTime() - d.getTime()) / 1000));

  if (deltaSec < 5) return lang === 'en' ? 'just now' : 'vừa xong';
  if (deltaSec < 60) {
    return lang === 'en' ? `${deltaSec} seconds ago` : `${deltaSec} giây trước`;
  }
  const min = Math.floor(deltaSec / 60);
  if (min < 60) {
    return lang === 'en' ? `${min} minutes ago` : `${min} phút trước`;
  }
  const hr = Math.floor(min / 60);
  if (hr < 24) {
    return lang === 'en' ? `${hr} hours ago` : `${hr} giờ trước`;
  }
  return fmtDate(d);
}

/**
 * Latency ladder per design canon: 247ms / 1.4s / 2.3m. The boundaries
 * compress to keep three-character displays in tables.
 */
export function fmtLatency(ms: number): string {
  if (ms < 1000) return `${Math.round(ms)}ms`;
  if (ms < 60_000) return `${(ms / 1000).toFixed(1)}s`;
  return `${(ms / 60_000).toFixed(1)}m`;
}

/**
 * Truncate-middle helper for idempotency keys / trace IDs. STYLING_SPECS §5:
 * mono 11px in tables; full string on hover (`title=`) + in detail
 * drawers. Truncation at >16 chars.
 */
export function fmtKeyTruncated(value: string, max = 16): string {
  if (value.length <= max) return value;
  return value.slice(0, max) + '…';
}
