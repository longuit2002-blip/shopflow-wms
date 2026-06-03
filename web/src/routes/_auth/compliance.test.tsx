/**
 * Compliance screen smoke + a11y test.
 *
 * Renders the route component (via Route.options.component — no extra export
 * needed) and pins the design-handoff anchors + axe-cleanliness. Anchor
 * assertions are locale-independent, so they hold regardless of the active
 * VN/EN locale.
 */

import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { render } from '@testing-library/react';
import { axe } from 'vitest-axe';
import type { ComponentType } from 'react';
import { __resetLocaleForTests } from '../../hooks/useLocale';
import { Route } from './compliance';

const Screen = (Route.options as { component: ComponentType }).component;

beforeEach(() => __resetLocaleForTests());
afterEach(() => __resetLocaleForTests());

describe('Compliance screen', () => {
  it('renders the design-anchored sections (header · residency · sub-processors)', () => {
    const { container } = render(<Screen />);
    expect(container.querySelector('[data-review="compliance-header"]')).not.toBeNull();
    expect(container.querySelector('[data-review="residency"]')).not.toBeNull();
    expect(container.querySelector('[data-review="subprocessors"]')).not.toBeNull();
  });

  it('surfaces the per-tenant database name (the isolation wedge)', () => {
    const { container } = render(<Screen />);
    expect(container.textContent).toContain('shopflow_yensaokhanhhoa');
  });

  it('has no axe violations', async () => {
    const { container } = render(<Screen />);
    expect(await axe(container)).toHaveNoViolations();
  });
});
