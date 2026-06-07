/**
 * Channels screen smoke + a11y test.
 */

import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { render } from '@testing-library/react';
import { axe } from 'vitest-axe';
import type { ComponentType } from 'react';
import { __resetLocaleForTests } from '../../hooks/useLocale';
import { Route } from './channels';

const Screen = (Route.options as { component: ComponentType }).component;

beforeEach(() => __resetLocaleForTests());
afterEach(() => __resetLocaleForTests());

describe('Channels screen', () => {
  it('renders the channel cards anchored surface', () => {
    const { container } = render(<Screen />);
    expect(container.querySelector('[data-review="channel-cards"]')).not.toBeNull();
  });

  it('has no axe violations', async () => {
    const { container } = render(<Screen />);
    expect(await axe(container)).toHaveNoViolations();
  });
});
