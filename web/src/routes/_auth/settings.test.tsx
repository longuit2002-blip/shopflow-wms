/**
 * Settings screen smoke + a11y test.
 *
 * Settings deep-links to /compliance + /audit via TanStack `<Link>`, which
 * needs a RouterProvider. For a render smoke test we stub `Link` as a plain
 * anchor while keeping the rest of the router module (createFileRoute) real.
 */

import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { render } from '@testing-library/react';
import { axe } from 'vitest-axe';
import type { ComponentType, ReactNode } from 'react';

vi.mock('@tanstack/react-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@tanstack/react-router')>();
  const { createElement } = await import('react');
  return {
    ...actual,
    Link: ({ to, children }: { to: unknown; children?: ReactNode }) =>
      createElement('a', { href: String(to) }, children),
  };
});

import { __resetLocaleForTests } from '../../hooks/useLocale';
import { Route } from './settings';

const Screen = (Route.options as { component: ComponentType }).component;

beforeEach(() => __resetLocaleForTests());
afterEach(() => __resetLocaleForTests());

describe('Settings screen', () => {
  it('renders the workspace settings cluster', () => {
    const { container } = render(<Screen />);
    expect(container.querySelector('[data-review="workspace"]')).not.toBeNull();
  });

  it('has no axe violations', async () => {
    const { container } = render(<Screen />);
    expect(await axe(container)).toHaveNoViolations();
  });
});
