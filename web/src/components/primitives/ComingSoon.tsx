/**
 * ComingSoon stub — rendered by every non-Inventory route in Sprint-6.
 *
 * Communicates that the screen is intentionally placeholdered, not broken
 * or missing. Shows: a Lucide icon, the screen name, a "Coming Sprint-X"
 * badge, and an optional roadmap blurb. Layout centered in the content
 * area so it works at any viewport ≥1024 px.
 *
 * Inputs are intentionally minimal so route files can declare a stub in
 * a single line: <ComingSoon icon={Boxes} screen="..." targetLabel="..." />.
 * Routes own their copy + sprint target; ComingSoon owns the look.
 */

import type { ComponentType, ReactNode } from 'react';

export interface ComingSoonProps {
  /**
   * Lucide icon component (e.g. `import { Boxes } from 'lucide-react'`).
   * Passed as a component reference rather than a string name so the
   * tree-shaker can drop unused icons.
   */
  icon: ComponentType<{ size?: number; strokeWidth?: number; 'aria-hidden'?: boolean }>;
  /** Screen name shown as the headline (already localized by caller). */
  screen: string;
  /** Sprint or phase target — e.g. "Sprint 7", "Phase 3". */
  targetLabel: string;
  /** Optional one-sentence roadmap context. */
  blurb?: ReactNode;
}

export function ComingSoon({ icon: Icon, screen, targetLabel, blurb }: ComingSoonProps) {
  return (
    <div
      style={{
        flex: 1,
        display: 'flex',
        flexDirection: 'column',
        alignItems: 'center',
        justifyContent: 'center',
        gap: 'var(--s-4)',
        padding: 'var(--s-8)',
        textAlign: 'center',
      }}
      data-coming-soon={screen}
    >
      <div
        style={{
          width: 56,
          height: 56,
          borderRadius: 'var(--radius-lg)',
          background: 'var(--bg-soft)',
          border: '1px solid var(--line)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          color: 'var(--ink-3)',
        }}
      >
        <Icon size={28} strokeWidth={1.5} aria-hidden />
      </div>

      <div className="t-xl" style={{ fontWeight: 600, color: 'var(--ink)' }}>
        {screen}
      </div>

      <span
        className="pill accent"
        style={{ fontSize: 'var(--text-xs)', height: 22, padding: '0 10px' }}
      >
        {targetLabel}
      </span>

      {blurb && (
        <div
          className="t-sm"
          style={{ color: 'var(--ink-2)', maxWidth: 420, lineHeight: 'var(--lh-base)' }}
        >
          {blurb}
        </div>
      )}
    </div>
  );
}
