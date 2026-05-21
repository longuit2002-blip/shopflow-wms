import { useState, type FormEvent } from 'react';
import { Logo } from '../primitives/Logo';
import { Button } from '../primitives/Button';
import { t, useLocale } from '../../hooks/useLocale';
import { LoginFailedError, resetPasswordConfirm } from '../../api/auth';

/**
 * Sprint-9.5 U6 — /reset-password screen. Reads the token from the URL
 * `?token=` query param; missing/malformed token shows an error panel
 * instead of the form. Submit calls `resetPasswordConfirm({token,
 * newPassword})` → 200 navigates to /login + toast; 422 inline error.
 *
 * Password complexity matches Sprint-8 R5: min 12 chars + 4-category
 * mix (upper / lower / digit / symbol). Enforced client-side as a
 * friendliness hint; backend re-validates authoritative.
 */
export interface ResetPasswordScreenProps {
  /** ?token= value parsed by the route component. */
  token: string | null;
  /** Fires on 200 — caller navigates to /login + raises a toast. */
  onResetComplete?: () => void;
  /** Fires when the token-error Continue button is clicked. */
  onRequestNewLink?: () => void;
}

type State =
  | { kind: 'idle' }
  | { kind: 'submitting' }
  | { kind: 'error'; message: string };

function passwordMeetsComplexity(pw: string): boolean {
  if (pw.length < 12) return false;
  const hasUpper = /[A-Z]/.test(pw);
  const hasLower = /[a-z]/.test(pw);
  const hasDigit = /[0-9]/.test(pw);
  const hasSymbol = /[^A-Za-z0-9]/.test(pw);
  return hasUpper && hasLower && hasDigit && hasSymbol;
}

export function ResetPasswordScreen({
  token,
  onResetComplete,
  onRequestNewLink,
}: ResetPasswordScreenProps) {
  useLocale();
  const [newPassword, setNewPassword] = useState('');
  const [confirm, setConfirm] = useState('');
  const [state, setState] = useState<State>({ kind: 'idle' });

  if (!token || token.trim().length === 0) {
    return (
      <div
        style={{
          minHeight: '100vh',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          background: 'var(--bg-soft)',
          padding: 'var(--s-6)',
        }}
      >
        <div
          className="card"
          style={{
            width: 400,
            padding: 'var(--s-7)',
            display: 'flex',
            flexDirection: 'column',
            gap: 'var(--s-4)',
          }}
          role="alert"
        >
          <Logo size={56} />
          <h1 className="t-xl" style={{ margin: 0, fontWeight: 600 }}>
            {t('Liên kết không hợp lệ', 'This link is invalid')}
          </h1>
          <p className="t-sm" style={{ margin: 0, color: 'var(--ink-2)' }}>
            {t(
              'Liên kết đặt lại này không hợp lệ hoặc đã được sử dụng. Yêu cầu một liên kết mới.',
              'This link is invalid or has been used. Request a new reset link.',
            )}
          </p>
          {onRequestNewLink && (
            <Button type="button" variant="primary" size="md" onClick={onRequestNewLink}>
              {t('Tiếp tục', 'Continue')}
            </Button>
          )}
        </div>
      </div>
    );
  }

  const canSubmit =
    newPassword.length >= 12
    && newPassword === confirm
    && state.kind !== 'submitting';

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!canSubmit) return;
    if (!passwordMeetsComplexity(newPassword)) {
      setState({
        kind: 'error',
        message: t(
          'Mật khẩu phải có ít nhất 12 ký tự, gồm chữ hoa, chữ thường, số và ký tự đặc biệt.',
          'Password must be at least 12 characters with upper, lower, digit, and symbol.',
        ),
      });
      return;
    }
    setState({ kind: 'submitting' });
    try {
      await resetPasswordConfirm({ token: token!, newPassword });
      onResetComplete?.();
    } catch (err) {
      const message =
        err instanceof LoginFailedError
          ? err.message
          : t('Không thể đặt lại mật khẩu.', 'Could not reset password.');
      setState({ kind: 'error', message });
    }
  }

  return (
    <div
      style={{
        minHeight: '100vh',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        background: 'var(--bg-soft)',
        padding: 'var(--s-6)',
      }}
    >
      <form
        onSubmit={handleSubmit}
        className="card"
        style={{
          width: 400,
          padding: 'var(--s-7)',
          display: 'flex',
          flexDirection: 'column',
          gap: 'var(--s-4)',
        }}
        aria-label={t('Đặt lại mật khẩu', 'Reset password')}
      >
        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
          <Logo size={56} />
          <h1 className="t-xl" style={{ margin: 'var(--s-3) 0 0', fontWeight: 600 }}>
            {t('Đặt lại mật khẩu', 'Reset password')}
          </h1>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-1)' }}>
          <label htmlFor="reset-new-password" className="lbl">
            {t('Mật khẩu mới', 'New password')}
          </label>
          <input
            id="reset-new-password"
            type="password"
            required
            autoComplete="new-password"
            minLength={12}
            value={newPassword}
            onChange={(e) => setNewPassword(e.target.value)}
            disabled={state.kind === 'submitting'}
          />
          <span className="t-xs" style={{ color: 'var(--ink-3)' }}>
            {t(
              'Tối thiểu 12 ký tự + chữ hoa + chữ thường + số + ký tự đặc biệt.',
              'Min 12 chars + upper + lower + digit + symbol.',
            )}
          </span>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-1)' }}>
          <label htmlFor="reset-confirm" className="lbl">
            {t('Xác nhận mật khẩu', 'Confirm password')}
          </label>
          <input
            id="reset-confirm"
            type="password"
            required
            autoComplete="new-password"
            value={confirm}
            onChange={(e) => setConfirm(e.target.value)}
            disabled={state.kind === 'submitting'}
          />
        </div>

        {state.kind === 'error' && (
          <div
            role="alert"
            className="t-sm"
            style={{
              padding: 'var(--s-2) var(--s-3)',
              borderRadius: 'var(--radius-md)',
              background: 'var(--danger-100)',
              border: '1px solid #E5A4A4',
              color: '#7A1A1A',
            }}
          >
            {state.message}
          </div>
        )}

        <Button
          type="submit"
          variant="primary"
          size="lg"
          disabled={!canSubmit}
          aria-busy={state.kind === 'submitting'}
        >
          {state.kind === 'submitting'
            ? t('Đang đặt lại…', 'Resetting…')
            : t('Đặt lại mật khẩu', 'Reset password')}
        </Button>
      </form>
    </div>
  );
}
