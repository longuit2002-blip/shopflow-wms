import { describe, it, expect, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { LedgerRow } from './LedgerRow';
import { __resetLocaleForTests, setLang } from '../../hooks/useLocale';
import type { SkuLedgerEntry } from '../../api/inventory';

afterEach(() => {
  __resetLocaleForTests();
});

const FIXTURE: SkuLedgerEntry = {
  id: '01HMABCDEF',
  orderId: 'order-abcdef-1234',
  orderLineId: 'line-99',
  status: 'Reserved',
  quantity: -5,
  timestamp: '2026-05-18T10:30:00Z',
  runningBalance: 95,
};

function renderRow(entry: SkuLedgerEntry) {
  return render(
    <table>
      <tbody>
        <LedgerRow entry={entry} />
      </tbody>
    </table>,
  );
}

describe('LedgerRow', () => {
  it('renders status pill with Vietnamese label by default', () => {
    renderRow(FIXTURE);
    expect(screen.getByText('Đã giữ')).toBeInTheDocument();
  });

  it('renders truncated order/line reference', () => {
    renderRow(FIXTURE);
    expect(screen.getByText(/order-abcdef/)).toBeInTheDocument();
  });

  it('prefixes negative quantities with a minus sign', () => {
    renderRow(FIXTURE);
    expect(screen.getByText('-5')).toBeInTheDocument();
  });

  it('prefixes positive quantities with a plus sign', () => {
    renderRow({ ...FIXTURE, quantity: 12, status: 'Adjusted' });
    expect(screen.getByText('+12')).toBeInTheDocument();
  });

  it('renders Confirmed status with its Vietnamese label', () => {
    renderRow({ ...FIXTURE, status: 'Confirmed' });
    expect(screen.getByText('Đã xác nhận')).toBeInTheDocument();
  });

  it('renders Released status with its Vietnamese label', () => {
    renderRow({ ...FIXTURE, status: 'Released' });
    expect(screen.getByText('Đã hoàn trả')).toBeInTheDocument();
  });

  it('falls back to the raw status when unknown', () => {
    renderRow({ ...FIXTURE, status: 'WeirdNewStatus' });
    expect(screen.getByText('WeirdNewStatus')).toBeInTheDocument();
  });

  it('uses English status label when locale=en', () => {
    setLang('en');
    renderRow(FIXTURE);
    expect(screen.getByText('Reserved')).toBeInTheDocument();
  });

  it('renders running balance', () => {
    renderRow(FIXTURE);
    expect(screen.getByText('95')).toBeInTheDocument();
  });
});
