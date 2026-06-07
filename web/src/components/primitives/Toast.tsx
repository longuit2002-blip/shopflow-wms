/**
 * Toast renderer — Sprint-6 plan U11.
 *
 * Pure projection of `useToast` state. Mounted once at the authenticated
 * layout level (`_auth.tsx`) so every screen inherits the same overlay.
 * STYLING_SPECS §7:
 *   - Bottom-right anchored, 12 px gap from the viewport edge.
 *   - Stack newest on top — list reverses on render.
 *   - 8 s dwell for errors, 4 s for success (owned by the store).
 *   - Error toast shows the idempotency key + trace id so the user can
 *     hand them to support during retry triage.
 *   - role="status" for success, role="alert" for error (so screen
 *     readers announce errors immediately).
 *
 * Manual dismiss is a single close (X) button. Click anywhere on the
 * toast body does NOT dismiss — too easy to trigger by accident.
 */

import { useToast, type Toast as ToastModel } from '../../hooks/useToast';
import { t, useLocale } from '../../hooks/useLocale';

export function ToastViewport() {
  useLocale();
  const toasts = useToast((s) => s.toasts);
  const dismiss = useToast((s) => s.dismiss);

  if (toasts.length === 0) return null;

  return (
    <div
      className="toast-viewport"
      data-testid="toast-viewport"
      aria-live="polite"
      aria-atomic="false"
      style={{
        position: 'fixed',
        right: 'var(--s-4)',
        bottom: 'var(--s-4)',
        display: 'flex',
        flexDirection: 'column-reverse',
        gap: 'var(--s-2)',
        zIndex: 'var(--z-toast)',
        pointerEvents: 'none',
      }}
    >
      {toasts.map((toast) => (
        <ToastItem key={toast.id} toast={toast} onDismiss={() => dismiss(toast.id)} />
      ))}
    </div>
  );
}

interface ToastItemProps {
  toast: ToastModel;
  onDismiss: () => void;
}

function ToastItem({ toast, onDismiss }: ToastItemProps) {
  const isError = toast.kind === 'error';
  return (
    <div
      role={isError ? 'alert' : 'status'}
      data-testid={`toast-${toast.kind}`}
      style={{
        minWidth: 260,
        maxWidth: 380,
        padding: 'var(--s-3) var(--s-4)',
        borderRadius: 'var(--radius-lg)',
        background: isError ? 'var(--danger-100)' : 'var(--success-100)',
        border: `1px solid ${isError ? 'var(--danger-500)' : 'var(--success-500)'}`,
        boxShadow: 'var(--shadow-modal)',
        color: isError ? 'var(--bad-ink)' : 'var(--ok-ink)',
        pointerEvents: 'auto',
        display: 'flex',
        flexDirection: 'column',
        gap: 'var(--s-1)',
        animation: 'toastSlideIn var(--duration-fast) var(--ease-out)',
      }}
    >
      <div style={{ display: 'flex', alignItems: 'flex-start', gap: 'var(--s-2)' }}>
        <div style={{ flex: '1 1 auto', fontWeight: 600 }}>{toast.title}</div>
        <button
          type="button"
          className="btn ghost sm"
          onClick={onDismiss}
          aria-label={t('Đóng thông báo', 'Dismiss notification')}
          style={{ flex: '0 0 auto', minWidth: 'auto', padding: '0 6px' }}
        >
          ✕
        </button>
      </div>
      {toast.body ? (
        <div className="t-sm" style={{ color: 'var(--ink-2)' }}>
          {toast.body}
        </div>
      ) : null}
      {isError && (toast.idempotencyKey || toast.traceId) ? (
        <div
          className="t-xs mono"
          data-testid="toast-trace"
          style={{ color: 'var(--ink-2)', wordBreak: 'break-all' }}
        >
          {toast.idempotencyKey ? (
            <div>
              {t('Mã idempotency', 'Idempotency')}: {toast.idempotencyKey}
            </div>
          ) : null}
          {toast.traceId ? (
            <div>
              {t('Mã trace', 'Trace')}: {toast.traceId}
            </div>
          ) : null}
        </div>
      ) : null}
    </div>
  );
}
