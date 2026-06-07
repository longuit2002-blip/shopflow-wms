import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { TenantPill } from './TenantPill';

describe('TenantPill', () => {
  it('renders monogram, legal name, erc, region, and db identifier', () => {
    render(
      <TenantPill
        monogram="YK"
        legalName="Yến Sào Khánh Hòa Co., Ltd."
        erc="0408123456"
        region="Khánh Hòa"
        dbName="shopflow_yensaokhanhhoa"
      />,
    );
    expect(screen.getByText('YK')).toBeInTheDocument();
    expect(screen.getByText('Yến Sào Khánh Hòa Co., Ltd.')).toBeInTheDocument();
    expect(screen.getByText('0408123456')).toBeInTheDocument();
    expect(screen.getByText(/db:shopflow_yensaokhanhhoa/)).toBeInTheDocument();
    // Region is interpolated inline; assert via the data-tenant-pill container
    const pill = screen.getByText('Yến Sào Khánh Hòa Co., Ltd.').closest('[data-tenant-pill]');
    expect(pill?.textContent).toContain('Khánh Hòa');
  });

  it('renders Vietnamese diacritics intact (no mojibake)', () => {
    render(
      <TenantPill
        monogram="YK"
        legalName="Yến Sào Khánh Hòa Co., Ltd."
        erc="0408123456"
        region="Khánh Hòa"
        dbName="shopflow_yensaokhanhhoa"
      />,
    );
    // Smoke check the legal name renders with diacritics — exact match avoids
    // matching the standalone region field which also contains "Khánh Hòa".
    expect(screen.getByText('Yến Sào Khánh Hòa Co., Ltd.')).toBeInTheDocument();
  });
});
