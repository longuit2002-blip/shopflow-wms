import { useMemo, useState, type FormEvent } from 'react';
import { Logo } from '../primitives/Logo';
import { Button } from '../primitives/Button';
import { t, useLocale } from '../../hooks/useLocale';
import { detectTenantFromHost, forgotPassword } from '../../api/auth';

/**
 * Sprint-9.5 U6 — /forgot-password screen. R6-disciplined: always shows
 * the same success state regardless of whether the email is on file.
 * Workspace auto-detected from the hostname (Sprint-8 detectTenantFromHost).
 */
export interface ForgotPasswordScreenProps {
  onBackToLogin?: () => void;
}

type State =
  | { kind: 'idle' }
  | { kind: 'submitting' }
  | { kind: 'success' }
  | { kind: 'error'; message: string };

export function ForgotPasswordScreen({ onBackToLogin }: ForgotPasswordScreenProps) {
  useLocale();
  const detectedTenant = useMemo(
    () => (typeof window !== 'undefined' ? detectTenantFromHost(window.location.hostname) : null),
    [],
  );
  const [email, setEmail] = useState('');
  const [tenantSlug, setTenantSlug] = useState(detectedTenant ?? '');
  const [state, setState] = useState<State>({ kind: 'idle' });

  const canSubmit =
    email.trim().length > 0
    && tenantSlug.trim().length > 0
    && state.kind !== 'submitting';

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!canSubmit) return;
    setState({ kind: 'submitting' });
    try {
      await forgotPassword({
        email: email.trim(),
        tenantSlug: tenantSlug.trim().toLowerCase(),
      });
      setState({ kind: 'success' });
    } catch {
      // R6 — even on a backend-side failure we render the same success
      // message so an attacker can't infer the email is on file.
      setState({ kind: 'success' });
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
        aria-label={t('Khôi phục mật khẩu', 'Forgot password')}
      >
        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
          <Logo size={56} />
          <h1 className="t-xl" style={{ margin: 'var(--s-3) 0 0', fontWeight: 600 }}>
            {t('Quên mật khẩu?', 'Forgot password?')}
          </h1>
        </div>

        {state.kind === 'success' ? (
          <>
            <div
              role="status"
              className="t-sm"
              style={{
                padding: 'var(--s-3)',
                borderRadius: 'var(--radius-md)',
                background: 'var(--bg-soft)',
                color: 'var(--ink-1)',
              }}
            >
              {t(
                'Nếu email của bạn được đăng ký, bạn sẽ nhận được liên kết đặt lại mật khẩu trong vòng 5 phút.',
                "If your email is on file, you'll receive a reset link within 5 minutes.",
              )}
            </div>
            {onBackToLogin && (
              <Button type="button" variant="secondary" size="md" onClick={onBackToLogin}>
                {t('Quay lại đăng nhập', 'Return to sign in')}
              </Button>
            )}
          </>
        ) : (
          <>
            <p className="t-sm" style={{ margin: 0, color: 'var(--ink-2)' }}>
              {t(
                'Nhập email và workspace, chúng tôi sẽ gửi liên kết đặt lại.',
                "Enter your email and workspace, we'll send you a reset link.",
              )}
            </p>

            <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-1)' }}>
              <label htmlFor="forgot-email" className="lbl">
                {t('Email', 'Email')}
              </label>
              <input
                id="forgot-email"
                type="email"
                required
                autoComplete="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                disabled={state.kind === 'submitting'}
              />
            </div>

            <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-1)' }}>
              <label htmlFor="forgot-tenant" className="lbl">
                {t('Workspace', 'Workspace')}
              </label>
              <input
                id="forgot-tenant"
                type="text"
                autoComplete="organization"
                required
                value={tenantSlug}
                onChange={(e) => setTenantSlug(e.target.value)}
                disabled={state.kind === 'submitting' || !!detectedTenant}
                readOnly={!!detectedTenant}
              />
            </div>

            <Button
              type="submit"
              variant="primary"
              size="lg"
              disabled={!canSubmit}
              aria-busy={state.kind === 'submitting'}
            >
              {state.kind === 'submitting'
                ? t('Đang gửi…', 'Sending…')
                : t('Gửi liên kết đặt lại', 'Send reset link')}
            </Button>

            {onBackToLogin && (
              <button
                type="button"
                onClick={onBackToLogin}
                style={{
                  background: 'none',
                  border: 'none',
                  padding: 0,
                  color: 'var(--accent-1)',
                  fontSize: 13,
                  cursor: 'pointer',
                  textDecoration: 'underline',
                }}
              >
                {t('Quay lại đăng nhập', 'Back to sign in')}
              </button>
            )}
          </>
        )}
      </form>
    </div>
  );
}
