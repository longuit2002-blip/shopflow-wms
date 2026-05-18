import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import { Boxes } from 'lucide-react';
import { ComingSoon } from './ComingSoon';

describe('ComingSoon', () => {
  it('renders screen name + target label', () => {
    render(<ComingSoon icon={Boxes} screen="Dashboard" targetLabel="Sprint 7" />);
    expect(screen.getByText('Dashboard')).toBeInTheDocument();
    expect(screen.getByText('Sprint 7')).toBeInTheDocument();
  });

  it('renders the icon (one svg via lucide-react)', () => {
    const { container } = render(
      <ComingSoon icon={Boxes} screen="Channels" targetLabel="Sprint 8" />,
    );
    expect(container.querySelector('svg')).toBeInTheDocument();
  });

  it('renders blurb when provided', () => {
    render(
      <ComingSoon
        icon={Boxes}
        screen="Reports"
        targetLabel="Phase 3"
        blurb="Analytics + KPI exports land in Phase 3."
      />,
    );
    expect(screen.getByText(/Analytics \+ KPI exports/i)).toBeInTheDocument();
  });

  it('omits blurb when not provided', () => {
    const { container } = render(
      <ComingSoon icon={Boxes} screen="Audit log" targetLabel="Phase 3" />,
    );
    // The blurb div is conditionally rendered; check we don't have stray "undefined" or empty text containers
    expect(container.querySelectorAll('.t-sm')).toHaveLength(0);
  });

  it('exposes the screen via data attribute for downstream selectors', () => {
    const { container } = render(
      <ComingSoon icon={Boxes} screen="Settings" targetLabel="Phase 3" />,
    );
    expect(container.querySelector('[data-coming-soon="Settings"]')).toBeInTheDocument();
  });
});
