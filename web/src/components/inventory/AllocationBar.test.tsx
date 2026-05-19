import { describe, it, expect, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { AllocationBar } from './AllocationBar';
import { __resetLocaleForTests, setLang } from '../../hooks/useLocale';

afterEach(() => {
  __resetLocaleForTests();
});

describe('AllocationBar', () => {
  it('renders the empty placeholder when allocations is []', () => {
    render(<AllocationBar allocations={[]} />);
    expect(screen.getByTestId('alloc-bar-empty')).toBeInTheDocument();
    expect(screen.getByText('Chưa có phân bổ kênh')).toBeInTheDocument();
  });

  it('renders the empty placeholder when total Allocated is zero', () => {
    render(
      <AllocationBar
        allocations={[
          { channel: 'Shopee', allocated: 0 },
          { channel: 'Lazada', allocated: 0 },
        ]}
      />,
    );
    expect(screen.getByTestId('alloc-bar-empty')).toBeInTheDocument();
  });

  it('renders 4 channel segments with proportional widths', () => {
    render(
      <AllocationBar
        allocations={[
          { channel: 'Shopee', allocated: 40 },
          { channel: 'Lazada', allocated: 30 },
          { channel: 'TikTok', allocated: 20 },
          { channel: 'Shopify', allocated: 10 },
        ]}
      />,
    );
    expect(screen.getByTestId('alloc-bar')).toBeInTheDocument();
    const shopee = document.querySelector('[data-channel="Shopee"]') as HTMLElement;
    const lazada = document.querySelector('[data-channel="Lazada"]') as HTMLElement;
    const tiktok = document.querySelector('[data-channel="TikTok"]') as HTMLElement;
    const shopify = document.querySelector('[data-channel="Shopify"]') as HTMLElement;
    expect(shopee).toHaveStyle({ width: '40%' });
    expect(lazada).toHaveStyle({ width: '30%' });
    expect(tiktok).toHaveStyle({ width: '20%' });
    expect(shopify).toHaveStyle({ width: '10%' });
  });

  it('lists each channel label with its rounded percentage', () => {
    render(
      <AllocationBar
        allocations={[
          { channel: 'Shopee', allocated: 50 },
          { channel: 'Lazada', allocated: 50 },
        ]}
      />,
    );
    expect(screen.getAllByText('50%')).toHaveLength(2);
    expect(screen.getByText('Shopee')).toBeInTheDocument();
    expect(screen.getByText('Lazada')).toBeInTheDocument();
  });

  it('renders Vietnamese channel names verbatim', () => {
    render(
      <AllocationBar
        allocations={[{ channel: 'Shopee', allocated: 100 }]}
      />,
    );
    expect(screen.getByText('Shopee')).toBeInTheDocument();
  });

  it('uses the English label when locale is en', () => {
    setLang('en');
    render(<AllocationBar allocations={[]} />);
    expect(screen.getByText('No channel allocation')).toBeInTheDocument();
  });

  it('falls back to neutral grey for unknown channel names', () => {
    render(
      <AllocationBar
        allocations={[{ channel: 'AmazonSEA', allocated: 100 }]}
      />,
    );
    const seg = document.querySelector('[data-channel="AmazonSEA"]') as HTMLElement;
    expect(seg.getAttribute('style') ?? '').toContain('--neutral-400');
  });
});
