/**
 * Audit log screen smoke + a11y test.
 *
 * Pins the design-handoff anchors (idempotency column + the JSON-diff
 * change-summary), the row → detail-drawer interaction, and axe-cleanliness.
 */

import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { render, fireEvent } from '@testing-library/react';
import { axe } from 'vitest-axe';
import type { ComponentType } from 'react';
import { __resetLocaleForTests } from '../../hooks/useLocale';
import { Route } from './audit';

const Screen = (Route.options as { component: ComponentType }).component;

beforeEach(() => __resetLocaleForTests());
afterEach(() => __resetLocaleForTests());

describe('Audit log screen', () => {
  it('renders the event table with the idempotency column', () => {
    const { container } = render(<Screen />);
    expect(container.querySelector('table.t-data.audit')).not.toBeNull();
    expect(container.querySelector('[data-review="idem"]')).not.toBeNull();
    // The drawer + its diff-stats are not present until a row is clicked.
    expect(container.querySelector('[data-review="diff-stats"]')).toBeNull();
  });

  it('opens the detail drawer with the JSON-diff change-summary on row click', () => {
    const { container } = render(<Screen />);
    const firstRow = container.querySelector('table.t-data.audit tbody tr');
    expect(firstRow).not.toBeNull();
    fireEvent.click(firstRow!);
    expect(container.querySelector('.drawer')).not.toBeNull();
    expect(container.querySelector('[data-review="diff-stats"]')).not.toBeNull();
  });

  it('has no axe violations', async () => {
    const { container } = render(<Screen />);
    expect(await axe(container)).toHaveNoViolations();
  });
});
