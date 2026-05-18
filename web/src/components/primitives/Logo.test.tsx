import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { Logo } from './Logo';

describe('Logo', () => {
  it('renders an SVG with role=img and aria-label', () => {
    render(<Logo />);
    const svg = screen.getByRole('img', { name: /ShopFlow logo/i });
    expect(svg).toBeInTheDocument();
    expect(svg.tagName.toLowerCase()).toBe('svg');
  });

  it('respects the size prop', () => {
    render(<Logo size={48} />);
    const svg = screen.getByRole('img');
    expect(svg.getAttribute('width')).toBe('48');
    expect(svg.getAttribute('height')).toBe('48');
  });

  it('renders 10 active dots (1-bits in the pattern)', () => {
    const { container } = render(<Logo />);
    // Pattern has 10 ones: 3 + 2 + 2 + 3
    expect(container.querySelectorAll('rect')).toHaveLength(10);
  });

  it('uses currentColor so callers can re-tint via CSS color', () => {
    const { container } = render(<Logo />);
    const firstRect = container.querySelector('rect');
    expect(firstRect?.getAttribute('fill')).toBe('currentColor');
  });

  it('accepts a custom title for assistive tech', () => {
    render(<Logo title="ShopFlow WMS mark" />);
    expect(screen.getByRole('img', { name: /ShopFlow WMS mark/i })).toBeInTheDocument();
  });
});
