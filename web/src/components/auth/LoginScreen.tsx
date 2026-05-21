/**
 * LoginScreen — single-screen login (Sprint-6 plan U5).
 *
 * Centered 400-px card on bg-soft with the dot-matrix Logo at 64 px,
 * email + password inputs, a disabled TOTP placeholder ("Mã 2FA"), and
 * an amber-ochre submit button. Any non-empty (email, password) pair
 * succeeds against the dev-mode `/auth/login` endpoint.
 *
 * On success: `useAuth.login(jwt)` populates the store + persists to
 * localStorage; the parent calls `onLoginSuccess()` to navigate. The
 * component is router-agnostic so the U6 route file can wrap it with
 * `useNavigate()` without changes here.
 *
 * STYLING_SPECS §6 a11y: focus-visible ring is wired in tokens.css
 * (U2); labels carry `htmlFor` + ids; the submit button announces a
 * loading state on press.
 */

import { useMemo, useState, type FormEvent } from 'react';
import { Logo } from '../primitives/Logo';
import { Button } from '../primitives/Button';
import { t, useLocale } from '../../hooks/useLocale';
import { useAuth } from '../../hooks/useAuth';
import { login, detectTenantFromHost } from '../../api/auth';

export interface LoginScreenProps {
  /** Fired after a `kind:'success'` login that promotes useAuth to `full-session`. */
  onLoginSuccess?: () => void;
  /** Sprint-9.5 — fired after a `kind:'mfa-challenge'` login (route to /mfa/challenge). */
  onMfaChallenge?: () => void;
  /** Sprint-9.5 — fired after a `kind:'mfa-enrollment'` login (route to /mfa/enroll). */
  onMfaEnrollment?: () => void;
  /** Sprint-9.5 — fired when user clicks "Forgot password?". */
  onForgotPassword?: () => void;
}

type SubmissionState =
  | { kind: 'idle' }
  | { kind: 'submitting' }
  | { kind: 'error'; message: string };

export function LoginScreen({
  onLoginSuccess,
  onMfaChallenge,
  onMfaEnrollment,
  onForgotPassword,
}: LoginScreenProps) {
  useLocale();
  const setSession = useAuth((s) => s.setSession);
  const setMfaChallenge = useAuth((s) => s.setMfaChallenge);
  const setMfaEnrollment = useAuth((s) => s.setMfaEnrollment);
  const detectedTenant = useMemo(
    () => (typeof window !== 'undefined' ? detectTenantFromHost(window.location.hostname) : null),
    [],
  );
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [tenantSlug, setTenantSlug] = useState(detectedTenant ?? '');
  const [rememberMe, setRememberMe] = useState(false);
  const [state, setState] = useState<SubmissionState>({ kind: 'idle' });

  const canSubmit =
    email.trim().length > 0
    && password.length > 0
    && tenantSlug.trim().length > 0
    && state.kind !== 'submitting';

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!canSubmit) return;
    setState({ kind: 'submitting' });
    const result = await login({
      email: email.trim(),
      password,
      rememberMe,
      tenantSlug: tenantSlug.trim().toLowerCase(),
    });
    switch (result.kind) {
      case 'success':
        setSession({
          accessToken: result.accessToken,
          refreshToken: result.refreshToken,
          accessTokenExpiresAt: result.accessTokenExpiresAt,
          refreshTokenExpiresAt: result.refreshTokenExpiresAt,
        });
        onLoginSuccess?.();
        return;
      case 'mfa-challenge':
        setMfaChallenge(result.intentToken, result.mfaMethods);
        onMfaChallenge?.();
        return;
      case 'mfa-enrollment':
        setMfaEnrollment(result.intentToken);
        onMfaEnrollment?.();
        return;
      case 'failure':
        setState({
          kind: 'error',
          message: result.message
            || t('Đăng nhập thất bại. Vui lòng thử lại.', 'Login failed. Please try again.'),
        });
        return;
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
        aria-label={t('Đăng nhập ShopFlow WMS', 'Sign in to ShopFlow WMS')}
      >
        <div
          style={{
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            gap: 'var(--s-2)',
          }}
        >
          <Logo size={64} />
          <h1 className="t-xl" style={{ margin: 0, fontWeight: 600 }}>
            ShopFlow WMS
          </h1>
          <p className="t-sm" style={{ margin: 0, color: 'var(--ink-2)' }}>
            {t('Đăng nhập để quản lý tồn kho', 'Sign in to manage your inventory')}
          </p>
        </div>

        <FormField id="login-email" label={t('Email', 'Email')}>
          <input
            id="login-email"
            type="email"
            required
            autoComplete="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            disabled={state.kind === 'submitting'}
          />
        </FormField>

        <FormField id="login-password" label={t('Mật khẩu', 'Password')}>
          <input
            id="login-password"
            type="password"
            required
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            disabled={state.kind === 'submitting'}
          />
        </FormField>

        <FormField
          id="login-tenant"
          label={t('Workspace', 'Workspace')}
          helper={detectedTenant
            ? t('Phát hiện từ tên miền', 'Detected from domain')
            : t('Nhập slug của workspace của bạn', 'Enter your workspace slug')}
        >
          <input
            id="login-tenant"
            type="text"
            autoComplete="organization"
            required
            value={tenantSlug}
            onChange={(e) => setTenantSlug(e.target.value)}
            disabled={state.kind === 'submitting' || !!detectedTenant}
            readOnly={!!detectedTenant}
          />
        </FormField>

        <label
          style={{
            display: 'flex',
            alignItems: 'center',
            gap: 'var(--s-2)',
            cursor: state.kind === 'submitting' ? 'default' : 'pointer',
            fontSize: 13,
            color: 'var(--ink-2)',
          }}
        >
          <input
            type="checkbox"
            checked={rememberMe}
            onChange={(e) => setRememberMe(e.target.checked)}
            disabled={state.kind === 'submitting'}
          />
          {t('Ghi nhớ phiên này 30 ngày', 'Stay signed in for 30 days')}
        </label>

        {onForgotPassword && (
          <button
            type="button"
            onClick={onForgotPassword}
            disabled={state.kind === 'submitting'}
            style={{
              background: 'none',
              border: 'none',
              padding: 0,
              color: 'var(--accent-1)',
              fontSize: 13,
              textAlign: 'left',
              cursor: 'pointer',
              textDecoration: 'underline',
            }}
          >
            {t('Quên mật khẩu?', 'Forgot password?')}
          </button>
        )}

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
            ? t('Đang đăng nhập…', 'Signing in…')
            : t('Đăng nhập', 'Sign in')}
        </Button>

        <p
          className="t-xs"
          style={{ margin: 0, color: 'var(--ink-3)', textAlign: 'center' }}
        >
          {t(
            'Phiên bản v0.9.0 · chế độ phát triển',
            'Version v0.9.0 · dev mode',
          )}
        </p>
      </form>
    </div>
  );
}

interface FormFieldProps {
  id: string;
  label: string;
  helper?: string;
  children: React.ReactNode;
}

function FormField({ id, label, helper, children }: FormFieldProps) {
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-1)' }}>
      <label htmlFor={id} className="lbl">
        {label}
      </label>
      {children}
      {helper && (
        <span className="t-xs" style={{ color: 'var(--ink-3)' }}>
          {helper}
        </span>
      )}
    </div>
  );
}
