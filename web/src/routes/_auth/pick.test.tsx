/**
 * Pick (operator) screen smoke + interaction + a11y test.
 *
 * The route renders no `<Link>`, so — unlike settings.test — no router mock
 * is needed; we pull the component straight off the route options. Default
 * locale after reset is Vietnamese, so text queries use the VN labels;
 * structural queries (role, aria-pressed, data-review) are locale-agnostic.
 */

import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import { axe } from 'vitest-axe';
import type { ComponentType } from 'react';

import { __resetLocaleForTests } from '../../hooks/useLocale';
import { Route } from './pick';

const Screen = (Route.options as { component: ComponentType }).component;

beforeEach(() => __resetLocaleForTests());
afterEach(() => __resetLocaleForTests());

describe('Pick (operator) screen', () => {
  it('renders the operator pick-wave surface', () => {
    const { container } = render(<Screen />);
    expect(container.querySelector('[data-review="operator-pick"]')).not.toBeNull();
  });

  it('surfaces wave progress as a progressbar', () => {
    render(<Screen />);
    const bar = screen.getByRole('progressbar');
    expect(bar.getAttribute('aria-valuemax')).toBe('100');
  });

  it('marks an item picked when its check button is pressed', () => {
    render(<Screen />);
    const pickedBefore = screen.getAllByRole('button', { pressed: true }).length;
    const firstUnpicked = screen.getAllByRole('button', { pressed: false })[0];
    expect(firstUnpicked).toBeTruthy();
    fireEvent.click(firstUnpicked!);
    expect(screen.getAllByRole('button', { pressed: true }).length).toBe(pickedBefore + 1);
  });

  it('opens the "cannot find" compensation modal', () => {
    render(<Screen />);
    const notFound = screen.getAllByRole('button', { name: /Không tìm thấy/ });
    expect(notFound.length).toBeGreaterThan(0);
    fireEvent.click(notFound[0]!);
    expect(screen.getByRole('dialog')).toBeTruthy();
  });

  it('has no axe violations', async () => {
    const { container } = render(<Screen />);
    expect(await axe(container)).toHaveNoViolations();
  });
});
