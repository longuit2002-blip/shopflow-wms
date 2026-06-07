/**
 * GuidedTour smoke + interaction + a11y test.
 *
 * Rendered standalone (no shell), so its 10 step anchors resolve to nothing
 * in jsdom — each step falls back to its centered "lives on screen X" card
 * rather than crashing. We assert the trigger → open → dialog flow + the
 * first note's copy. Default locale after reset is Vietnamese.
 */

import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { axe } from 'vitest-axe';

import { __resetLocaleForTests } from '../../hooks/useLocale';
import { GuidedTour } from './GuidedTour';

beforeEach(() => __resetLocaleForTests());
afterEach(() => __resetLocaleForTests());

describe('GuidedTour', () => {
  it('renders a trigger and no dialog until opened', () => {
    render(<GuidedTour />);
    expect(screen.getByRole('button', { name: /hướng dẫn/i })).toBeTruthy();
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('opens a step dialog with the first design note on trigger click', () => {
    render(<GuidedTour />);
    fireEvent.click(screen.getByRole('button', { name: /Mở hướng dẫn/i }));
    const dialog = screen.getByRole('dialog');
    expect(dialog).toBeTruthy();
    // Step 1 of 10 + the first note's title.
    expect(dialog.textContent).toContain('1 / 10');
    expect(dialog.textContent).toMatch(/Amber-ochre/i);
  });

  it('advances to the next note when Next is pressed', () => {
    render(<GuidedTour />);
    fireEvent.click(screen.getByRole('button', { name: /Mở hướng dẫn/i }));
    fireEvent.click(screen.getByRole('button', { name: /Bước tiếp/i }));
    expect(screen.getByRole('dialog').textContent).toContain('2 / 10');
  });

  it('closes on Escape', () => {
    render(<GuidedTour />);
    fireEvent.click(screen.getByRole('button', { name: /Mở hướng dẫn/i }));
    expect(screen.getByRole('dialog')).toBeTruthy();
    fireEvent.keyDown(window, { key: 'Escape' });
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('has no axe violations while open', async () => {
    const { container } = render(<GuidedTour />);
    fireEvent.click(screen.getByRole('button', { name: /Mở hướng dẫn/i }));
    expect(await axe(container)).toHaveNoViolations();
  });
});
