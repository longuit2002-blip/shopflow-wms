/**
 * AdjustStockModal — Sprint-6 plan U11 (R8).
 *
 * Composes the Modal primitive with a stock-adjustment form. Owner role
 * opens this via the "Điều chỉnh tồn" button in `<LedgerDrawer>`'s header.
 *
 * Form fields:
 *   - delta (number) — signed integer; positive recount/found, negative
 *     damage/theft. Submit disabled when 0 (no-op) or empty.
 *   - reason (enum) — one of recount/damage/theft/found/other. Submit
 *     disabled when empty (no default; force a deliberate choice).
 *   - note (string, optional) — free-form audit note, max 240 chars.
 *
 * Submission:
 *   - Calls `useInventoryMutations.adjust.mutateAsync({ sku, delta, reason, note })`.
 *   - On success: invalidates inventory queries (handled by the hook) +
 *     closes the modal so the SKU table's 2-s poll surfaces the new
 *     value within ~2 s (Sprint-6 trade-off #1 acceptance — restart
 *     loses non-real DB columns but on_hand round-trips through the
 *     stock-adjustments path).
 *   - On error: keeps the modal open so the user can retry from the same
 *     form state. The error toast is pushed by the hook.
 *
 * Backdrop dismiss is disabled so a stray click outside doesn't lose
 * a half-typed delta; Esc and the X button still close.
 */

import { useState, useId, type FormEvent } from 'react';
import { Modal } from '../primitives/Modal';
import { useInventoryMutations } from '../../hooks/useInventoryMutations';
import { t, useLocale } from '../../hooks/useLocale';

export interface AdjustStockModalProps {
  isOpen: boolean;
  onClose: () => void;
  sku: string;
}

type Reason = 'recount' | 'damage' | 'theft' | 'found' | 'other';

const NOTE_MAX_LENGTH = 240;

export function AdjustStockModal({ isOpen, onClose, sku }: AdjustStockModalProps) {
  useLocale();
  const [delta, setDelta] = useState<string>('');
  const [reason, setReason] = useState<Reason | ''>('');
  const [note, setNote] = useState('');
  const deltaId = useId();
  const reasonId = useId();
  const noteId = useId();
  const { adjust } = useInventoryMutations();

  function reset(): void {
    setDelta('');
    setReason('');
    setNote('');
    adjust.reset();
  }

  function handleClose(): void {
    if (adjust.isPending) return;
    reset();
    onClose();
  }

  const parsedDelta = Number.parseInt(delta, 10);
  const deltaIsValid = !Number.isNaN(parsedDelta) && parsedDelta !== 0;
  const submitDisabled = !deltaIsValid || reason === '' || adjust.isPending;

  async function handleSubmit(e: FormEvent<HTMLFormElement>): Promise<void> {
    e.preventDefault();
    if (submitDisabled) return;
    try {
      await adjust.mutateAsync({
        sku,
        delta: parsedDelta,
        reason: reason as Reason,
        ...(note.trim() ? { note: note.trim() } : {}),
      });
      reset();
      onClose();
    } catch {
      // The hook pushes an error toast; keep the modal open with values intact.
    }
  }

  return (
    <Modal
      isOpen={isOpen}
      onClose={handleClose}
      title={`${t('Điều chỉnh tồn', 'Adjust stock')} · ${sku}`}
      dismissOnBackdrop={false}
      footer={
        <>
          <button
            type="button"
            className="btn ghost"
            onClick={handleClose}
            disabled={adjust.isPending}
          >
            {t('Hủy', 'Cancel')}
          </button>
          <button
            type="submit"
            form="adjust-stock-form"
            className="btn primary"
            disabled={submitDisabled}
            data-testid="adjust-submit"
          >
            {adjust.isPending
              ? t('Đang lưu…', 'Saving…')
              : t('Lưu điều chỉnh', 'Save adjustment')}
          </button>
        </>
      }
    >
      <form
        id="adjust-stock-form"
        onSubmit={handleSubmit}
        style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-4)' }}
        noValidate
      >
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-1)' }}>
          <label htmlFor={deltaId} className="lbl">
            {t('Chênh lệch', 'Delta')}
          </label>
          <input
            id={deltaId}
            type="number"
            inputMode="numeric"
            autoComplete="off"
            spellCheck={false}
            value={delta}
            onChange={(e) => setDelta(e.target.value)}
            placeholder="+10, -3, …"
            aria-describedby={`${deltaId}-help`}
            data-testid="adjust-delta"
            style={{ width: '100%' }}
          />
          <div id={`${deltaId}-help`} className="t-xs" style={{ color: 'var(--ink-2)' }}>
            {t(
              'Số dương = tăng tồn; số âm = giảm tồn. Không nhận giá trị 0.',
              'Positive = increase stock; negative = decrease stock. Zero rejected.',
            )}
          </div>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-1)' }}>
          <label htmlFor={reasonId} className="lbl">
            {t('Lý do', 'Reason')}
          </label>
          <select
            id={reasonId}
            value={reason}
            onChange={(e) => setReason(e.target.value as Reason | '')}
            data-testid="adjust-reason"
            style={{ width: '100%' }}
          >
            <option value="">{t('— Chọn lý do —', '— Select reason —')}</option>
            <option value="recount">{t('Kiểm kê lại', 'Recount')}</option>
            <option value="damage">{t('Hư hỏng', 'Damage')}</option>
            <option value="theft">{t('Mất / thất thoát', 'Theft / loss')}</option>
            <option value="found">{t('Tìm thấy thêm', 'Found surplus')}</option>
            <option value="other">{t('Khác', 'Other')}</option>
          </select>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-1)' }}>
          <label htmlFor={noteId} className="lbl">
            {t('Ghi chú (tùy chọn)', 'Note (optional)')}
          </label>
          <textarea
            id={noteId}
            value={note}
            onChange={(e) => setNote(e.target.value.slice(0, NOTE_MAX_LENGTH))}
            rows={3}
            maxLength={NOTE_MAX_LENGTH}
            autoComplete="off"
            placeholder={t('Ví dụ: kiểm kho cuối tuần…', 'e.g. weekend recount…')}
            data-testid="adjust-note"
            style={{ width: '100%', resize: 'vertical', minHeight: 64 }}
          />
          <div
            className="t-xs"
            style={{ color: 'var(--ink-3)', textAlign: 'right' }}
            aria-live="polite"
          >
            {note.length} / {NOTE_MAX_LENGTH}
          </div>
        </div>
      </form>
    </Modal>
  );
}
