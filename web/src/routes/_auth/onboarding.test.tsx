/**
 * Onboarding screen smoke + a11y test.
 *
 * Renders the provisioning wizard at its first step and pins the step-1
 * anchor + axe-cleanliness. (The provisioning state machine only starts
 * after a user action, so the on-mount render is static.)
 */

import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { render } from '@testing-library/react';
import { axe } from 'vitest-axe';
import type { ComponentType } from 'react';
import { __resetLocaleForTests } from '../../hooks/useLocale';
import { Route } from './onboarding';

const Screen = (Route.options as { component: ComponentType }).component;

beforeEach(() => __resetLocaleForTests());
afterEach(() => __resetLocaleForTests());

describe('Onboarding screen', () => {
  it('renders the wizard at step 1', () => {
    const { container } = render(<Screen />);
    expect(container.querySelector('[data-review="onboarding-step-1"]')).not.toBeNull();
  });

  it('has no axe violations', async () => {
    const { container } = render(<Screen />);
    expect(await axe(container)).toHaveNoViolations();
  });
});
