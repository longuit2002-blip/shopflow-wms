/**
 * Dashboard screen smoke + a11y test.
 *
 * The dashboard runs a 1s SLA-age ticker; the test renders, asserts the
 * border-card design anchor (note 07 — borders not shadows), and runs axe.
 */

import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { render } from '@testing-library/react';
import { axe } from 'vitest-axe';
import type { ComponentType } from 'react';
import { __resetLocaleForTests } from '../../hooks/useLocale';
import { Route } from './dashboard';

const Screen = (Route.options as { component: ComponentType }).component;

beforeEach(() => __resetLocaleForTests());
afterEach(() => __resetLocaleForTests());

describe('Dashboard screen', () => {
  it('renders the border-card anchored surface (design note 07)', () => {
    const { container } = render(<Screen />);
    expect(container.querySelector('[data-review="border-card"]')).not.toBeNull();
  });

  it('has no axe violations', async () => {
    const { container } = render(<Screen />);
    expect(await axe(container)).toHaveNoViolations();
  });
});
