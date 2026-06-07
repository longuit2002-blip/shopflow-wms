import { useState, type FormEvent } from 'react';
import { Logo } from '../primitives/Logo';
import { Button } from '../primitives/Button';
import { t, useLocale } from '../../hooks/useLocale';
import { useAuth, type MfaMethod } from '../../hooks/useAuth';
import { LoginFailedError, verifyMfa } from '../../api/auth';

/**
 * Sprint-9.5 U6 — /mfa/challenge. Gated by useAuth.authState ===
 * 'mfa-challenge'. Auto-focus OTP input + "Use recovery code instead"
 * toggle. After 3 consecutive bad OTPs (or recovery codes), clears the
 * intent token + redirects via onSessionExpired.
 */
export interface MfaChallengeScreenProps {
  /** Fired after a successful verify that promotes useAuth to full-session. */
  onSuccess?: () => void;
  /** Fired after 3 bad codes — caller redirects to /login + raises a toast. */
  onSessionExpired?: () => void;
}

const MAX_ATTEMPTS = 3;

export function MfaChallengeScreen({ onSuccess, onSessionExpired }: MfaChallengeScreenProps) {
  useLocale();
  const intentToken = useAuth((s) => s.intentToken);
  const setSession = useAuth((s) => s.setSession);
  const clearIntent = useAuth((s) => s.clearIntent);

  const [code, setCode] = useState('');
  const [method, setMethod] = useState<MfaMethod>('totp');
  const [attempts, setAttempts] = useState(0);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const expectedLength = method === 'totp' ? 6 : 8;
  const canSubmit =
    !!intentToken
    && code.trim().length === expectedLength
    && !submitting;

  async function handleSubmit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (!canSubmit) return;
    setSubmitting(true);
    setError(null);
    try {
      const result = await verifyMfa(intentToken!, {
        code: code.trim(),
        method,
      });
      setSession({
        accessToken: result.accessToken,
        accessTokenExpiresAt: result.accessTokenExpiresAt,
        refreshToken: result.refreshToken,
        refreshTokenExpiresAt: result.refreshTokenExpiresAt,
      });
      onSuccess?.();
    } catch (err) {
      const nextAttempts = attempts + 1;
      setAttempts(nextAttempts);
      const message =
        err instanceof LoginFailedError
          ? err.message
          : t('Mã không hợp lệ.', 'Invalid code.');
      if (nextAttempts >= MAX_ATTEMPTS) {
        clearIntent();
        onSessionExpired?.();
        return;
      }
      setError(message);
      setCode('');
    } finally {
      setSubmitting(false);
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
        aria-label={t('Xác thực 2 yếu tố', 'Two-factor authentication')}
      >
        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
          <Logo size={56} />
          <h1 className="t-xl" style={{ margin: 'var(--s-3) 0 0', fontWeight: 600 }}>
            {method === 'totp'
              ? t('Nhập mã xác thực', 'Enter your code')
              : t('Nhập mã khôi phục', 'Enter a recovery code')}
          </h1>
          <p className="t-sm" style={{ margin: '4px 0 0', color: 'var(--ink-2)' }}>
            {method === 'totp'
              ? t(
                  'Mở ứng dụng xác thực và nhập mã 6 chữ số.',
                  'Open your authenticator app and enter the 6-digit code.',
                )
              : t('Nhập 1 trong 10 mã khôi phục bạn đã lưu.', 'Enter one of your 10 saved codes.')}
          </p>
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-1)' }}>
          <label htmlFor="mfa-code" className="lbl">
            {method === 'totp' ? t('Mã 6 chữ số', '6-digit code') : t('Mã khôi phục', 'Recovery code')}
          </label>
          <input
            id="mfa-code"
            type="text"
            autoFocus
            inputMode={method === 'totp' ? 'numeric' : 'text'}
            autoComplete="one-time-code"
            required
            value={code}
            onChange={(e) => setCode(e.target.value)}
            disabled={submitting}
            maxLength={expectedLength}
            pattern={method === 'totp' ? '\\d{6}' : '[A-Za-z0-9-]{8}'}
            style={{ fontFamily: 'monospace', fontSize: 18 }}
          />
        </div>

        {error && (
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
            {error}
          </div>
        )}

        <Button
          type="submit"
          variant="primary"
          size="lg"
          disabled={!canSubmit}
          aria-busy={submitting}
        >
          {submitting
            ? t('Đang xác minh…', 'Verifying…')
            : t('Xác minh', 'Verify')}
        </Button>

        <button
          type="button"
          onClick={() => {
            setMethod(method === 'totp' ? 'recovery' : 'totp');
            setCode('');
            setError(null);
          }}
          disabled={submitting}
          style={{
            background: 'none',
            border: 'none',
            padding: 0,
            color: 'var(--accent-1)',
            fontSize: 13,
            cursor: 'pointer',
            textDecoration: 'underline',
            textAlign: 'center',
          }}
        >
          {method === 'totp'
            ? t('Dùng mã khôi phục thay thế', 'Use a recovery code instead')
            : t('Quay lại mã xác thực', 'Back to authenticator code')}
        </button>
      </form>
    </div>
  );
}
