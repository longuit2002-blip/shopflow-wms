/**
 * Modal primitive — Sprint-6 plan U11.
 *
 * Centered overlay used by `<AdjustStockModal>` (U11) and `<CreateSkuModal>`
 * (U12). Mirrors the Drawer primitive's a11y / motion contract:
 *   - role="dialog" + aria-modal="true" + aria-labelledby pointing at the
 *     visible title (or aria-label if no title is set).
 *   - Esc closes (document listener, survives focus inside inputs).
 *   - Click on backdrop closes — disabled when `dismissOnBackdrop=false`
 *     so forms with unsaved changes don't lose state to an accidental
 *     click outside.
 *   - Focus moves to the close button on open; Tab cycles within the
 *     modal's focusable descendants (basic trap).
 *   - 150 ms pop-in via `modalPopIn` keyframe (tokens.css §motion).
 *
 * Render contract: `isOpen=false` → returns `null` + unmounts children.
 * No exit animation in Sprint-6 (re-mount cost is negligible against the
 * 150 ms enter window). Width defaults to 480 px per STYLING_SPECS §7;
 * the Create-SKU modal (U12) bumps to 640 px.
 */

import { useEffect, useId, useRef, type ReactNode } from 'react';
import { t } from '../../hooks/useLocale';

const FOCUSABLE_SELECTOR =
  'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

const DEFAULT_WIDTH = 480;

export interface ModalProps {
  isOpen: boolean;
  onClose: () => void;
  /** Plain-text title rendered in the header and referenced by aria-labelledby. */
  title: string;
  /** Pixel width; defaults to 480 px per STYLING_SPECS §7. */
  width?: number;
  /** If false, clicking the backdrop does NOT close the modal. Defaults to true. */
  dismissOnBackdrop?: boolean;
  /** Optional footer node rendered below the body (e.g. action buttons). */
  footer?: ReactNode;
  children: ReactNode;
}

export function Modal({
  isOpen,
  onClose,
  title,
  width = DEFAULT_WIDTH,
  dismissOnBackdrop = true,
  footer,
  children,
}: ModalProps) {
  const modalRef = useRef<HTMLDivElement | null>(null);
  const closeButtonRef = useRef<HTMLButtonElement | null>(null);
  const titleId = useId();

  useEffect(() => {
    if (!isOpen) return;
    const modal = modalRef.current;
    if (!modal) return;

    closeButtonRef.current?.focus();

    function handleKey(e: KeyboardEvent) {
      if (e.key !== 'Tab') return;
      const focusables = Array.from(
        modal!.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR),
      );
      if (focusables.length === 0) {
        e.preventDefault();
        return;
      }
      const first = focusables[0]!;
      const last = focusables[focusables.length - 1]!;
      const active = document.activeElement as HTMLElement | null;
      if (e.shiftKey && active === first) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && active === last) {
        e.preventDefault();
        first.focus();
      }
    }

    modal.addEventListener('keydown', handleKey);
    return () => modal.removeEventListener('keydown', handleKey);
  }, [isOpen]);

  useEffect(() => {
    if (!isOpen) return;
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        e.preventDefault();
        // Capture-phase + stopImmediatePropagation so a Modal opened
        // over a Drawer (or over another Modal) consumes Esc itself
        // instead of also closing the surface underneath. Drawer's Esc
        // listener registers in bubble phase, so capture wins.
        e.stopImmediatePropagation();
        onClose();
      }
    }
    document.addEventListener('keydown', onKey, { capture: true });
    return () => document.removeEventListener('keydown', onKey, { capture: true });
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  return (
    <>
      <div
        className="modal-mask"
        data-testid="modal-mask"
        aria-hidden="true"
        onClick={dismissOnBackdrop ? onClose : undefined}
        style={{ animation: 'drawerMaskFadeIn var(--duration-fast) var(--ease-out)' }}
      />
      <div
        ref={modalRef}
        className="modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        style={{
          width,
          animation: 'modalPopIn var(--duration-fast) var(--ease-out)',
        }}
      >
        <header
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 'var(--s-3)',
            padding: 'var(--s-4) var(--s-5)',
            borderBottom: '1px solid var(--neutral-200)',
            flex: '0 0 auto',
          }}
        >
          <h2
            id={titleId}
            style={{
              margin: 0,
              fontSize: 'var(--text-lg)',
              lineHeight: 'var(--lh-lg)',
              fontWeight: 600,
              color: 'var(--neutral-800)',
              flex: '1 1 auto',
              minWidth: 0,
              overflow: 'hidden',
              textOverflow: 'ellipsis',
              whiteSpace: 'nowrap',
            }}
          >
            {title}
          </h2>
          <button
            type="button"
            ref={closeButtonRef}
            className="btn ghost sm"
            onClick={onClose}
            aria-label={t('Đóng', 'Close')}
            style={{ flex: '0 0 auto' }}
          >
            ✕
          </button>
        </header>
        <div
          style={{
            padding: 'var(--s-4) var(--s-5)',
            overflowY: 'auto',
            minHeight: 0,
            flex: '1 1 auto',
          }}
        >
          {children}
        </div>
        {footer ? (
          <footer
            style={{
              display: 'flex',
              justifyContent: 'flex-end',
              gap: 'var(--s-2)',
              padding: 'var(--s-3) var(--s-5)',
              borderTop: '1px solid var(--neutral-200)',
              flex: '0 0 auto',
            }}
          >
            {footer}
          </footer>
        ) : null}
      </div>
    </>
  );
}
