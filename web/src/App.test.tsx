import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import App from './App';
import { __resetLocaleForTests } from './hooks/useLocale';

describe('App (Sprint-6 U3 shell)', () => {
  beforeEach(() => {
    __resetLocaleForTests();
  });

  afterEach(() => {
    __resetLocaleForTests();
  });

  it('renders the desktop shell (sidebar nav + topbar banner)', () => {
    render(<App />);
    expect(screen.getByRole('banner')).toBeInTheDocument();
    expect(screen.getByRole('navigation', { name: /Điều hướng chính/i })).toBeInTheDocument();
  });

  it('starts on the Inventory placeholder slot', () => {
    render(<App />);
    const heading = screen.getByRole('heading', { name: /Tồn kho/i });
    expect(heading).toBeInTheDocument();
  });

  it('renders ComingSoon when user clicks a stubbed nav item', async () => {
    const user = userEvent.setup();
    render(<App />);
    await user.click(screen.getByRole('button', { name: /Tổng quan/i }));
    expect(screen.getByText(/Sắp ra mắt/i)).toBeInTheDocument();
  });

  it('flips locale on EN button click and re-renders labels', async () => {
    const user = userEvent.setup();
    render(<App />);
    await user.click(screen.getByRole('button', { name: /English/i }));
    expect(screen.getByRole('heading', { name: /Inventory/i })).toBeInTheDocument();
  });
});
