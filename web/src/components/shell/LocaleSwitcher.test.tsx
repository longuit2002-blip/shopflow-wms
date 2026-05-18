import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { LocaleSwitcher } from './LocaleSwitcher';
import { __resetLocaleForTests, getLang } from '../../hooks/useLocale';

describe('LocaleSwitcher', () => {
  beforeEach(() => {
    __resetLocaleForTests();
  });

  afterEach(() => {
    __resetLocaleForTests();
  });

  it('renders both VI and EN buttons with the VI option pressed by default', () => {
    render(<LocaleSwitcher />);
    const vi = screen.getByRole('button', { name: /Tiếng Việt/i });
    const en = screen.getByRole('button', { name: /English/i });
    expect(vi).toHaveAttribute('aria-pressed', 'true');
    expect(en).toHaveAttribute('aria-pressed', 'false');
  });

  it('flips locale on EN click and persists to localStorage + html lang', async () => {
    const user = userEvent.setup();
    render(<LocaleSwitcher />);
    await user.click(screen.getByRole('button', { name: /English/i }));

    expect(getLang()).toBe('en');
    expect(window.localStorage.getItem('shopflow.lang')).toBe('en');
    expect(document.documentElement.lang).toBe('en-US');
  });

  it('updates aria-pressed after click', async () => {
    const user = userEvent.setup();
    render(<LocaleSwitcher />);
    await user.click(screen.getByRole('button', { name: /English/i }));

    expect(screen.getByRole('button', { name: /English/i })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
    expect(screen.getByRole('button', { name: /Tiếng Việt/i })).toHaveAttribute(
      'aria-pressed',
      'false',
    );
  });
});
