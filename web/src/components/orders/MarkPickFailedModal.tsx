/**
 * MarkPickFailedModal — Sprint-11 plan U2.
 *
 * Captures the picker's free-text reason before calling
 * `useMarkPickFailedMutation`. Reuses the Sprint-6 KTD9 `Modal` primitive
 * (capture-phase Esc + focus trap + role="dialog") so it composes with
 * the order-detail surface — never `window.prompt` (DL-001).
 *
 * Validation:
 *   - reason must be non-empty (trim). Submit button disabled until
 *     populated; no submit-then-error spinner round-trip.
 *
 * Pending state:
 *   - `isPending=true` from the parent disables both buttons + shows
 *     loading copy on the confirm button.
 *
 * Mirrors the `CreateSkuModal` shape so the two surfaces stay reviewable
 * together; the Sprint-6 Modal a11y contract (label association, Esc
 * capture, focus trap) carries through unchanged.
 */

import { useEffect, useId, useState, type FormEvent } from 'react';
import { Modal } from '../primitives/Modal';
import { t, useLocale } from '../../hooks/useLocale';

export interface MarkPickFailedModalProps {
  isOpen: boolean;
  onClose: () => void;
  onSubmit: (reason: string) => void;
  isPending: boolean;
}

export function MarkPickFailedModal({
  isOpen,
  onClose,
  onSubmit,
  isPending,
}: MarkPickFailedModalProps) {
  useLocale();
  const [reason, setReason] = useState('');
  const reasonId = useId();

  // Reset the textarea every time the modal opens so the second-time
  // picker doesn't see the previous failure's text.
  useEffect(() => {
    if (isOpen) setReason('');
  }, [isOpen]);

  const trimmed = reason.trim();
  const submitDisabled = trimmed.length === 0 || isPending;

  function handleClose(): void {
    if (isPending) return;
    onClose();
  }

  function handleSubmit(e: FormEvent<HTMLFormElement>): void {
    e.preventDefault();
    if (submitDisabled) return;
    onSubmit(trimmed);
  }

  return (
    <Modal
      isOpen={isOpen}
      onClose={handleClose}
      title={t('Báo lỗi lấy hàng', 'Mark pick failed')}
      dismissOnBackdrop={false}
      footer={
        <>
          <button
            type="button"
            className="btn ghost"
            onClick={handleClose}
            disabled={isPending}
            data-testid="mark-pick-failed-cancel"
          >
            {t('Hủy', 'Cancel')}
          </button>
          <button
            type="submit"
            form="mark-pick-failed-form"
            className="btn danger"
            disabled={submitDisabled}
            aria-busy={isPending ? true : undefined}
            data-testid="mark-pick-failed-submit"
          >
            {isPending
              ? t('Đang gửi…', 'Submitting…')
              : t('Báo lỗi', 'Mark failed')}
          </button>
        </>
      }
    >
      <form
        id="mark-pick-failed-form"
        onSubmit={handleSubmit}
        style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-3)' }}
        noValidate
      >
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-1)' }}>
          <label htmlFor={reasonId} className="lbl">
            {t('Lý do', 'Reason')}
          </label>
          <textarea
            id={reasonId}
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            placeholder={t(
              'VD: Hết hàng trên kệ, thiếu nhãn, …',
              'e.g. Out of stock on shelf, missing label, …',
            )}
            rows={4}
            autoComplete="off"
            spellCheck={false}
            disabled={isPending}
            aria-describedby={`${reasonId}-help`}
            data-testid="mark-pick-failed-reason"
            style={{ width: '100%', resize: 'vertical', minHeight: 80 }}
          />
          <div
            id={`${reasonId}-help`}
            className="t-xs"
            style={{ color: 'var(--ink-2)' }}
          >
            {t(
              'Lý do sẽ được ghi vào lịch sử bù trừ saga.',
              'Reason is recorded in the saga compensation history.',
            )}
          </div>
        </div>
      </form>
    </Modal>
  );
}
