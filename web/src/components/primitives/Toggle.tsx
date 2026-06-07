/**
 * Toggle primitive — Sprint-6 plan U12.
 *
 * Two-state switch used by `<FlashSaleToggle>` (U12) and any future
 * boolean-flip surface (notification mute, demo-mode etc.).
 *
 * Controlled: the parent owns `checked` and the `onChange(next)` setter.
 * The toggle does NOT manage its own state — that keeps the optimistic-
 * UI integration trivial (FlashSaleToggle holds the optimistic value).
 *
 * Visual contract (STYLING_SPECS §7):
 *   - Desktop: track 32 × 18 px, thumb 14 × 14 px.
 *   - Touch (when `.touch` class is on a parent): track 44 × 24 px,
 *     thumb 18 × 18 px.
 *   - Background: --accent when on, --neutral-200 when off.
 *   - 150 ms transition on thumb position + track colour.
 *
 * A11y:
 *   - role="switch" + aria-checked exposes the binary state.
 *   - Label is required (either `label` prop OR aria-label) so screen
 *     readers announce the affordance.
 *   - Space + Enter toggle (handled natively by <button type="button">
 *     since browsers map Space → click on buttons).
 *   - Focus-visible inherits the global ring from tokens.css.
 *
 * Disabled state: rendered with `aria-disabled` + `disabled` attribute;
 * clicks + keyboard activation are no-ops.
 */

import type { ReactNode } from 'react';

export interface ToggleProps {
  checked: boolean;
  onChange: (next: boolean) => void;
  /** Accessible label rendered next to the toggle. Required unless `ariaLabel` is set. */
  label?: ReactNode;
  /** Standalone aria-label when no visible label is desired. */
  ariaLabel?: string;
  disabled?: boolean;
  'data-testid'?: string;
}

export function Toggle({
  checked,
  onChange,
  label,
  ariaLabel,
  disabled,
  'data-testid': testId,
}: ToggleProps) {
  function handleClick(): void {
    if (disabled) return;
    onChange(!checked);
  }

  return (
    <label
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 'var(--s-2)',
        cursor: disabled ? 'not-allowed' : 'pointer',
        opacity: disabled ? 0.6 : 1,
      }}
    >
      <button
        type="button"
        role="switch"
        aria-checked={checked}
        aria-label={ariaLabel}
        aria-disabled={disabled || undefined}
        disabled={disabled}
        onClick={handleClick}
        data-testid={testId}
        style={{
          position: 'relative',
          width: 32,
          height: 18,
          padding: 0,
          borderRadius: 999,
          border: '1px solid',
          borderColor: checked ? 'var(--accent-line)' : 'var(--neutral-300)',
          background: checked ? 'var(--accent)' : 'var(--neutral-200)',
          transition:
            'background var(--duration-fast) var(--ease-out), border-color var(--duration-fast) var(--ease-out)',
          flex: '0 0 auto',
        }}
      >
        <span
          aria-hidden="true"
          style={{
            position: 'absolute',
            top: 1,
            left: checked ? 15 : 1,
            width: 14,
            height: 14,
            borderRadius: '50%',
            background: '#FFFFFF',
            boxShadow: '0 1px 2px rgba(0, 0, 0, 0.18)',
            transition: 'left var(--duration-fast) var(--ease-out)',
          }}
        />
      </button>
      {label != null ? <span>{label}</span> : null}
    </label>
  );
}
