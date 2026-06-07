import { describe, it, expect, vi } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Toggle } from './Toggle';

describe('Toggle', () => {
  it('renders with role=switch and aria-checked reflecting the prop', () => {
    render(<Toggle checked={false} onChange={() => {}} ariaLabel="Flash sale" />);
    const sw = screen.getByRole('switch');
    expect(sw).toHaveAttribute('aria-checked', 'false');
  });

  it('renders aria-checked=true when checked', () => {
    render(<Toggle checked onChange={() => {}} ariaLabel="Flash sale" />);
    expect(screen.getByRole('switch')).toHaveAttribute('aria-checked', 'true');
  });

  it('clicking the switch calls onChange with the opposite value', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Toggle checked={false} onChange={onChange} ariaLabel="Flash sale" />);
    await user.click(screen.getByRole('switch'));
    expect(onChange).toHaveBeenCalledTimes(1);
    expect(onChange).toHaveBeenCalledWith(true);
  });

  it('clicking when checked=true calls onChange(false)', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Toggle checked onChange={onChange} ariaLabel="Flash sale" />);
    await user.click(screen.getByRole('switch'));
    expect(onChange).toHaveBeenCalledWith(false);
  });

  it('Space toggles (native button behaviour)', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(<Toggle checked={false} onChange={onChange} ariaLabel="Flash sale" />);
    screen.getByRole('switch').focus();
    await user.keyboard(' ');
    expect(onChange).toHaveBeenCalledWith(true);
  });

  it('renders the visible label next to the switch', () => {
    render(<Toggle checked onChange={() => {}} label="Flash-sale routing" />);
    expect(screen.getByText('Flash-sale routing')).toBeInTheDocument();
  });

  it('disabled toggle does not call onChange on click', async () => {
    const onChange = vi.fn();
    const user = userEvent.setup();
    render(
      <Toggle checked={false} onChange={onChange} ariaLabel="Flash sale" disabled />,
    );
    await user.click(screen.getByRole('switch'));
    expect(onChange).not.toHaveBeenCalled();
  });

  it('disabled toggle carries aria-disabled and the disabled attribute', () => {
    render(<Toggle checked={false} onChange={() => {}} ariaLabel="A" disabled />);
    const sw = screen.getByRole('switch');
    expect(sw).toHaveAttribute('aria-disabled', 'true');
    expect(sw).toBeDisabled();
  });

  it('thumb visually slides — left offset differs between off and on', () => {
    const { rerender } = render(
      <Toggle checked={false} onChange={() => {}} ariaLabel="A" />,
    );
    const sw = screen.getByRole('switch');
    const offThumb = sw.querySelector('span');
    expect(offThumb).not.toBeNull();
    const offLeft = offThumb!.getAttribute('style') ?? '';

    rerender(<Toggle checked onChange={() => {}} ariaLabel="A" />);
    const onThumb = screen.getByRole('switch').querySelector('span');
    const onLeft = onThumb!.getAttribute('style') ?? '';

    expect(offLeft).toMatch(/left:\s*1px/);
    expect(onLeft).toMatch(/left:\s*15px/);
  });

  it('forwards data-testid to the switch button for targeting', () => {
    render(
      <Toggle
        checked={false}
        onChange={() => {}}
        ariaLabel="A"
        data-testid="flash-toggle"
      />,
    );
    expect(screen.getByTestId('flash-toggle')).toHaveAttribute('role', 'switch');
  });
});
