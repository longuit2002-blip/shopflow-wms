import { describe, it, expect } from 'vitest';
import {
  fmtVND,
  fmtNum,
  fmtDate,
  fmtTime,
  fmtAge,
  fmtLatency,
  fmtKeyTruncated,
} from './format';

describe('fmtVND', () => {
  it('formats with vi-VN thousands separator + NBSP + ₫', () => {
    const result = fmtVND(28_410_000);
    expect(result).toContain('₫');
    expect(result).toMatch(/28[.,]410[.,]000/);
  });
});

describe('fmtNum', () => {
  it('uses period thousands separator in vi-VN', () => {
    expect(fmtNum(1234567, 'vi')).toMatch(/1[.,]234[.,]567/);
  });

  it('uses comma thousands separator in en-US', () => {
    expect(fmtNum(1234567, 'en')).toBe('1,234,567');
  });
});

describe('fmtDate', () => {
  it('formats as dd/mm/yyyy', () => {
    expect(fmtDate(new Date(2026, 4, 28))).toBe('28/05/2026');
  });
});

describe('fmtTime', () => {
  it('formats as 24-h HH:MM', () => {
    expect(fmtTime(new Date(2026, 4, 28, 14, 32))).toBe('14:32');
  });
});

describe('fmtAge', () => {
  const now = new Date('2026-05-28T12:00:00');

  it('returns "vừa xong" for < 5 seconds', () => {
    expect(fmtAge(new Date('2026-05-28T11:59:58'), 'vi', now)).toBe('vừa xong');
  });

  it('returns "N giây trước" for < 60 seconds', () => {
    expect(fmtAge(new Date('2026-05-28T11:59:30'), 'vi', now)).toBe('30 giây trước');
  });

  it('returns "N phút trước" for < 60 minutes', () => {
    expect(fmtAge(new Date('2026-05-28T11:30:00'), 'vi', now)).toBe('30 phút trước');
  });

  it('falls back to absolute date for > 24 hours', () => {
    expect(fmtAge(new Date('2026-05-26T12:00:00'), 'vi', now)).toBe('26/05/2026');
  });

  it('honors English locale', () => {
    expect(fmtAge(new Date('2026-05-28T11:30:00'), 'en', now)).toBe('30 minutes ago');
  });
});

describe('fmtLatency', () => {
  it('uses ms below 1 second', () => {
    expect(fmtLatency(247)).toBe('247ms');
  });

  it('uses s between 1 and 60 seconds', () => {
    expect(fmtLatency(1400)).toBe('1.4s');
  });

  it('uses m above 60 seconds', () => {
    expect(fmtLatency(138_000)).toBe('2.3m');
  });
});

describe('fmtKeyTruncated', () => {
  it('returns as-is when length ≤ max', () => {
    expect(fmtKeyTruncated('short', 16)).toBe('short');
  });

  it('truncates with ellipsis when length > max', () => {
    expect(fmtKeyTruncated('idem_01HKDX_42_p3_x4f3', 16)).toBe('idem_01HKDX_42_p…');
  });
});
