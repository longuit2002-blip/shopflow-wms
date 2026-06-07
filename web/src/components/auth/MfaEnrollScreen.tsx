import { useEffect, useState, type FormEvent } from 'react';
import { Logo } from '../primitives/Logo';
import { Button } from '../primitives/Button';
import { t, useLocale } from '../../hooks/useLocale';
import { useAuth } from '../../hooks/useAuth';
import { beginEnroll, verifyEnroll, type MfaEnrollBeginResponse } from '../../api/auth';
import { RecoveryCodesDisplay } from './RecoveryCodesDisplay';

/**
 * Sprint-9.5 U6 — /mfa/enroll. Gated by useAuth.authState ===
 * 'mfa-enrollment'. 3-step vertical stepper:
 *   1. Begin enrollment — `beginEnroll(intentToken)` returns the QR SVG
 *      (Cache-Control: no-store per KTD16) + manual secret.
 *   2. User scans + enters 6-digit OTP → `verifyEnroll(intentToken,
 *      enrollmentId, otp)` returns the access/refresh pair + 10
 *      recovery codes.
 *   3. RecoveryCodesDisplay forces ack-then-Continue before promoting
 *      the session to full-session via useAuth.setSession.
 */
export interface MfaEnrollScreenProps {
  onEnrollmentComplete?: () => void;
  onSessionExpired?: () => void;
}

type Step =
  | { kind: 'loading-qr' }
  | { kind: 'qr-loaded'; qr: MfaEnrollBeginResponse }
  | { kind: 'verifying'; qr: MfaEnrollBeginResponse; otp: string }
  | {
      kind: 'recovery-codes';
      recoveryCodes: readonly string[];
    };

export function MfaEnrollScreen({
  onEnrollmentComplete,
  onSessionExpired,
}: MfaEnrollScreenProps) {
  useLocale();
  const intentToken = useAuth((s) => s.intentToken);
  const setSession = useAuth((s) => s.setSession);
  const clearIntent = useAuth((s) => s.clearIntent);

  const [step, setStep] = useState<Step>({ kind: 'loading-qr' });
  const [otp, setOtp] = useState('');
  const [error, setError] = useState<string | null>(null);

  // Step 1 — fetch QR.
  useEffect(() => {
    if (!intentToken) {
      onSessionExpired?.();
      return;
    }
    let cancelled = false;
    (async () => {
      try {
        const qr = await beginEnroll(intentToken);
        if (!cancelled) setStep({ kind: 'qr-loaded', qr });
      } catch {
        if (!cancelled) {
          clearIntent();
          onSessionExpired?.();
        }
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [intentToken, clearIntent, onSessionExpired]);

  async function handleVerify(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (step.kind !== 'qr-loaded') return;
    if (otp.trim().length !== 6) return;
    setStep({ kind: 'verifying', qr: step.qr, otp });
    setError(null);
    try {
      const result = await verifyEnroll(intentToken!, {
        enrollmentId: step.qr.enrollmentId,
        otp: otp.trim(),
      });
      // Stash the session pair to apply on RecoveryCodesDisplay Continue.
      pendingSession.set({
        accessToken: result.accessToken,
        accessTokenExpiresAt: result.accessTokenExpiresAt,
        refreshToken: result.refreshToken,
        refreshTokenExpiresAt: result.refreshTokenExpiresAt,
      });
      setStep({ kind: 'recovery-codes', recoveryCodes: result.recoveryCodes });
    } catch {
      setError(t('Mã không hợp lệ. Thử lại.', 'Invalid code. Try again.'));
      setStep({ kind: 'qr-loaded', qr: step.qr });
    }
  }

  function handleContinue() {
    const session = pendingSession.get();
    if (session) {
      setSession(session);
      pendingSession.clear();
    }
    onEnrollmentComplete?.();
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
      <div
        className="card"
        style={{
          width: 520,
          padding: 'var(--s-7)',
          display: 'flex',
          flexDirection: 'column',
          gap: 'var(--s-4)',
        }}
        aria-label={t('Đăng ký 2FA', 'Enroll MFA')}
      >
        <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center' }}>
          <Logo size={56} />
          <h1 className="t-xl" style={{ margin: 'var(--s-3) 0 0', fontWeight: 600 }}>
            {step.kind === 'recovery-codes'
              ? t('Lưu mã khôi phục', 'Save your recovery codes')
              : t('Đăng ký xác thực 2 yếu tố', 'Enable two-factor authentication')}
          </h1>
        </div>

        {step.kind === 'loading-qr' && (
          <p className="t-sm" role="status" style={{ color: 'var(--ink-2)' }}>
            {t('Đang chuẩn bị QR…', 'Preparing QR code…')}
          </p>
        )}

        {(step.kind === 'qr-loaded' || step.kind === 'verifying') && (
          <form
            onSubmit={handleVerify}
            style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-4)' }}
          >
            <section style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-2)' }}>
              <h2 className="t-md" style={{ margin: 0 }}>
                {t('Bước 1 — Quét mã QR', 'Step 1 — Scan the QR code')}
              </h2>
              <div
                data-testid="enrollment-qr"
                aria-label={t('Mã QR đăng ký', 'Enrollment QR code')}
                role="img"
                dangerouslySetInnerHTML={{ __html: step.qr.qrSvg }}
                style={{
                  background: '#fff',
                  padding: 'var(--s-3)',
                  borderRadius: 'var(--radius-md)',
                  alignSelf: 'center',
                }}
              />
              <p className="t-xs" style={{ color: 'var(--ink-3)', margin: 0 }}>
                {t(
                  'Không quét được? Nhập thủ công khóa bên dưới.',
                  "Can't scan? Enter the key manually below.",
                )}
              </p>
              <code
                style={{
                  fontFamily: 'monospace',
                  fontSize: 13,
                  padding: 'var(--s-2) var(--s-3)',
                  background: 'var(--bg-soft)',
                  borderRadius: 'var(--radius-md)',
                  wordBreak: 'break-all',
                }}
              >
                {step.qr.manualSecret}
              </code>
            </section>

            <section style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-2)' }}>
              <h2 className="t-md" style={{ margin: 0 }}>
                {t(
                  'Bước 2 — Nhập mã 6 chữ số',
                  'Step 2 — Enter the 6-digit code',
                )}
              </h2>
              <input
                id="enroll-otp"
                aria-label={t('Mã 6 chữ số', '6-digit code')}
                type="text"
                autoComplete="one-time-code"
                inputMode="numeric"
                required
                maxLength={6}
                pattern="\d{6}"
                value={otp}
                onChange={(e) => setOtp(e.target.value)}
                disabled={step.kind === 'verifying'}
                style={{ fontFamily: 'monospace', fontSize: 18 }}
              />
              {error && (
                <div role="alert" className="t-sm" style={{ color: '#7A1A1A' }}>
                  {error}
                </div>
              )}
              <Button
                type="submit"
                variant="primary"
                size="lg"
                disabled={otp.trim().length !== 6 || step.kind === 'verifying'}
                aria-busy={step.kind === 'verifying'}
              >
                {step.kind === 'verifying'
                  ? t('Đang xác minh…', 'Verifying…')
                  : t('Xác minh', 'Verify')}
              </Button>
            </section>
          </form>
        )}

        {step.kind === 'recovery-codes' && (
          <RecoveryCodesDisplay
            codes={step.recoveryCodes}
            onContinue={handleContinue}
          />
        )}
      </div>
    </div>
  );
}

// Module-scoped pending session — bridges the verify-step result to the
// RecoveryCodesDisplay Continue handler without leaking it into state
// (the codes display is a stable identity; we don't want a re-render
// while the user is reading their codes).
const pendingSession: {
  get: () => Parameters<ReturnType<typeof useAuth.getState>['setSession']>[0] | null;
  set: (s: Parameters<ReturnType<typeof useAuth.getState>['setSession']>[0]) => void;
  clear: () => void;
} = (() => {
  let value: Parameters<ReturnType<typeof useAuth.getState>['setSession']>[0] | null = null;
  return {
    get: () => value,
    set: (s) => {
      value = s;
    },
    clear: () => {
      value = null;
    },
  };
})();
