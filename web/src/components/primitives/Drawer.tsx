/**
 * Drawer primitive — Sprint-6 plan U10.
 *
 * Reusable right-side drawer used by LedgerDrawer (U10). Designed for
 * reuse in Sprint-7+ Order detail / Channel detail surfaces, so the
 * primitive owns motion + a11y + focus management and the composing
 * component owns the body content.
 *
 * Behaviour:
 *   - Slides in from the right via the `drawerSlideIn` keyframe
 *     (`var(--duration-fast)` = 150 ms per STYLING_SPECS §4).
 *   - Backdrop mask uses the canon `.drawer-mask` class + fade-in.
 *   - Esc closes (listener at document level so it works even when
 *     focus is inside an input that swallows the keydown).
 *   - Click on backdrop closes.
 *   - Close (X) button in header closes.
 *   - role="dialog" + aria-modal="true" + aria-labelledby pointing at the
 *     visible title; STYLING_SPECS §6.4 compliant.
 *   - Focus moves to the close button on open; Tab cycles within the
 *     drawer's focusable descendants (basic trap — sufficient for the
 *     vertical-slice surface; full roving-tabindex is Sprint-7+).
 *
 * Render contract: when `isOpen=false`, returns `null` and unmounts
 * children. No exit animation in Sprint-6 — re-mount cost on open is
 * negligible against the 150 ms slide-in window.
 */

import { useEffect, useId, useRef, type ReactNode } from 'react';
import { t } from '../../hooks/useLocale';

const FOCUSABLE_SELECTOR =
  'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

const DEFAULT_WIDTH = 620;

export interface DrawerProps {
  isOpen: boolean;
  onClose: () => void;
  /** Plain-text title rendered in the header and referenced by aria-labelledby. */
  title: string;
  /** Optional slot rendered between the title and the close button (e.g. a toggle). */
  headerExtra?: ReactNode;
  /** Pixel width; defaults to 620 px per STYLING_SPECS §7. */
  width?: number;
  children: ReactNode;
}

export function Drawer({
  isOpen,
  onClose,
  title,
  headerExtra,
  width = DEFAULT_WIDTH,
  children,
}: DrawerProps) {
  const drawerRef = useRef<HTMLDivElement | null>(null);
  const closeButtonRef = useRef<HTMLButtonElement | null>(null);
  const titleId = useId();

  useEffect(() => {
    if (!isOpen) return;
    const drawer = drawerRef.current;
    if (!drawer) return;

    closeButtonRef.current?.focus();

    function handleKey(e: KeyboardEvent) {
      if (e.key !== 'Tab') return;
      const focusables = Array.from(
        drawer!.querySelectorAll<HTMLElement>(FOCUSABLE_SELECTOR),
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

    drawer.addEventListener('keydown', handleKey);
    return () => drawer.removeEventListener('keydown', handleKey);
  }, [isOpen]);

  useEffect(() => {
    if (!isOpen) return;
    function onKey(e: KeyboardEvent) {
      if (e.key === 'Escape') {
        e.preventDefault();
        onClose();
      }
    }
    document.addEventListener('keydown', onKey);
    return () => document.removeEventListener('keydown', onKey);
  }, [isOpen, onClose]);

  if (!isOpen) return null;

  return (
    <>
      {/*
        Backdrop is decorative + mouse-only shortcut to close — keyboard
        users get the close button (focused on mount) and Esc (document
        listener). aria-hidden keeps it out of the a11y tree; the
        jsx-a11y plugin accepts a click handler on an aria-hidden div.
      */}
      <div
        className="drawer-mask"
        data-testid="drawer-mask"
        aria-hidden="true"
        onClick={onClose}
        style={{ animation: 'drawerMaskFadeIn var(--duration-fast) var(--ease-out)' }}
      />
      <aside
        ref={drawerRef}
        className="drawer"
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        style={{
          width,
          animation: 'drawerSlideIn var(--duration-fast) var(--ease-out)',
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
          {headerExtra ? <div style={{ flex: '0 0 auto' }}>{headerExtra}</div> : null}
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
        <div style={{ flex: 1, overflowY: 'auto', minHeight: 0 }}>{children}</div>
      </aside>
    </>
  );
}
