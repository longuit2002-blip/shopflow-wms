/**
 * ThresholdInlineEdit — Sprint-6 plan U11 (R9).
 *
 * Inline cell editor for the SKU table's "Threshold" column. Owner role
 * clicks (or focuses + Enter on) the cell → input replaces the value
 * → Enter or blur commits via PUT, Esc cancels.
 *
 * Optimistic UI:
 *   - On commit, the input flips back to the "display" state showing the
 *     new value immediately. The mutation fires in the background; on
 *     error, the value reverts to the previous server value and the
 *     hook surfaces the error toast (idempotency-key + trace-id).
 *   - The SKU table re-fetches every 2 s anyway, so a missed optimistic
 *     update self-corrects within one poll window even without the
 *     mutation's explicit query invalidation.
 *
 * Keyboard / a11y:
 *   - The static cell is a `button` so Tab reaches it and Enter / Space
 *     enters edit mode.
 *   - The input has aria-label so screen readers announce "Threshold
 *     for SKU YN-001".
 *   - Esc reverts (closes the editor without saving); Enter or blur
 *     commits.
 */

import { useEffect, useRef, useState, type KeyboardEvent } from 'react';
import { useInventoryMutations } from '../../hooks/useInventoryMutations';
import { t, useLocale } from '../../hooks/useLocale';
import { usePerm } from '../../hooks/usePerm';
import { fmtNum } from '../../lib/format';

export interface ThresholdInlineEditProps {
  sku: string;
  value: number | null;
}

export function ThresholdInlineEdit({ sku, value }: ThresholdInlineEditProps) {
  const { lang } = useLocale();
  const { setThreshold } = useInventoryMutations();
  // Sprint-10.5 U5 — DL-001 fallback: when the user lacks the
  // threshold-write perm, render the numeric value as a static <span>
  // (same tabular-nums style as the active button) so the column keeps
  // its width and the value stays legible. `return null` would delete the
  // value from the row entirely. Uses `usePerm` (reactive — KTD3).
  const canEdit = usePerm('inventory.skus.threshold.write');
  const [isEditing, setIsEditing] = useState(false);
  const [draft, setDraft] = useState<string>(value == null ? '' : String(value));
  const [optimisticValue, setOptimisticValue] = useState<number | null>(value);
  // Track the last server-driven value we saw, so we can detect "the
  // server value changed" without a useEffect. This is the React 19
  // "Adjusting State Based on Props" pattern — set state during render
  // with a previous-prop guard. Avoids an extra render the useEffect
  // approach would cost (per Vercel `rerender-derived-state-no-effect`).
  const [lastServerValue, setLastServerValue] = useState<number | null>(value);
  const inputRef = useRef<HTMLInputElement | null>(null);
  const committedRef = useRef(false);

  if (lastServerValue !== value) {
    setLastServerValue(value);
    setOptimisticValue(value);
    if (!isEditing) {
      setDraft(value == null ? '' : String(value));
    }
  }

  // Focus the input when entering edit mode. Effect, not derived,
  // because focus is a DOM side effect, not state.
  useEffect(() => {
    if (isEditing) {
      inputRef.current?.focus();
      inputRef.current?.select();
    }
  }, [isEditing]);

  function enterEdit(): void {
    committedRef.current = false;
    setDraft(value == null ? '' : String(value));
    setIsEditing(true);
  }

  function cancel(): void {
    setIsEditing(false);
    setDraft(value == null ? '' : String(value));
  }

  async function commit(): Promise<void> {
    if (committedRef.current) return;
    committedRef.current = true;
    const parsed = Number.parseInt(draft, 10);
    if (Number.isNaN(parsed) || parsed < 0) {
      cancel();
      return;
    }
    if (parsed === value) {
      setIsEditing(false);
      return;
    }
    setOptimisticValue(parsed);
    setIsEditing(false);
    try {
      await setThreshold.mutateAsync({ sku, threshold: parsed });
    } catch {
      // Revert — useEffect above will restore from server `value` once
      // the next poll lands; also force-set here in case the user is
      // staring at a stale render in the meantime.
      setOptimisticValue(value);
    }
  }

  function handleKey(e: KeyboardEvent<HTMLInputElement>): void {
    if (e.key === 'Enter') {
      e.preventDefault();
      void commit();
    } else if (e.key === 'Escape') {
      e.preventDefault();
      cancel();
    }
  }

  // DL-001: when gated-hidden, render a static <span> mirroring the
  // tabular-nums style so the column width is preserved without giving
  // the appearance of an interactive control.
  if (!canEdit) {
    return (
      <span
        data-testid={`threshold-static-${sku}`}
        style={{
          display: 'inline-block',
          minWidth: 60,
          textAlign: 'right',
          fontVariantNumeric: 'tabular-nums',
          padding: '0 6px',
        }}
      >
        {value != null ? fmtNum(value, lang) : '—'}
      </span>
    );
  }

  if (isEditing) {
    return (
      <input
        ref={inputRef}
        type="number"
        min={0}
        inputMode="numeric"
        autoComplete="off"
        spellCheck={false}
        value={draft}
        onChange={(e) => setDraft(e.target.value)}
        onBlur={() => void commit()}
        onKeyDown={handleKey}
        aria-label={`${t('Mức an toàn cho', 'Threshold for')} ${sku}`}
        data-testid={`threshold-input-${sku}`}
        style={{
          width: 80,
          textAlign: 'right',
          fontVariantNumeric: 'tabular-nums',
        }}
      />
    );
  }

  return (
    <button
      type="button"
      onClick={enterEdit}
      aria-label={`${t('Sửa mức an toàn cho', 'Edit threshold for')} ${sku}`}
      data-testid={`threshold-cell-${sku}`}
      className="btn ghost sm"
      style={{
        padding: '0 6px',
        minWidth: 60,
        justifyContent: 'flex-end',
        fontVariantNumeric: 'tabular-nums',
        color: 'inherit',
        opacity: setThreshold.isPending ? 0.6 : 1,
      }}
    >
      {optimisticValue != null ? fmtNum(optimisticValue, lang) : '—'}
    </button>
  );
}
