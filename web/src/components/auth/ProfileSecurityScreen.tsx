import { useState, type FormEvent } from 'react';
import { Button } from '../primitives/Button';
import { t, useLocale } from '../../hooks/useLocale';
import { useAuth } from '../../hooks/useAuth';
import {
  changePassword,
  disableMfa,
  regenerateRecoveryCodes,
  LoginFailedError,
} from '../../api/auth';
import { RecoveryCodesDisplay } from './RecoveryCodesDisplay';

/**
 * Sprint-9.5 U6 — /profile/security. Three cards: MFA status, recovery
 * codes regen, change password. Gated by useAuth.authState ===
 * 'full-session' via the parent route.
 *
 * Sprint-9.5 limits: the screen reads the user's MFA-enrolled status
 * from a caller-supplied prop (e.g. /api/users/me) — Sprint-10+ may
 * surface this from a dedicated useMe() hook + cache. For now the
 * caller wires it.
 */
export interface ProfileSecurityScreenProps {
  /** Caller-supplied MFA-enrolled state (Sprint-10+ migrates to useMe()). */
  mfaEnrolled: boolean;
  onMfaResetRequest?: () => void;
}

type ChangePwState =
  | { kind: 'idle' }
  | { kind: 'submitting' }
  | { kind: 'success' }
  | { kind: 'error'; message: string };

export function ProfileSecurityScreen({
  mfaEnrolled,
  onMfaResetRequest,
}: ProfileSecurityScreenProps) {
  useLocale();
  const user = useAuth((s) => s.user);

  const [regeneratedCodes, setRegeneratedCodes] = useState<readonly string[] | null>(null);
  const [regenLoading, setRegenLoading] = useState(false);

  const [disableModalOpen, setDisableModalOpen] = useState(false);
  const [disablePassword, setDisablePassword] = useState('');
  const [disableSubmitting, setDisableSubmitting] = useState(false);
  const [disableError, setDisableError] = useState<string | null>(null);

  const [currentPw, setCurrentPw] = useState('');
  const [newPw, setNewPw] = useState('');
  const [confirmPw, setConfirmPw] = useState('');
  const [pwState, setPwState] = useState<ChangePwState>({ kind: 'idle' });

  async function handleRegenerate() {
    setRegenLoading(true);
    try {
      const result = await regenerateRecoveryCodes();
      setRegeneratedCodes(result.recoveryCodes);
    } finally {
      setRegenLoading(false);
    }
  }

  async function handleDisableConfirm(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (disablePassword.length === 0) return;
    setDisableSubmitting(true);
    setDisableError(null);
    try {
      await disableMfa(disablePassword);
      setDisableModalOpen(false);
      setDisablePassword('');
      onMfaResetRequest?.();
    } catch (err) {
      setDisableError(
        err instanceof LoginFailedError
          ? err.message
          : t('Không thể tắt MFA.', 'Could not disable MFA.'),
      );
    } finally {
      setDisableSubmitting(false);
    }
  }

  async function handleChangePassword(e: FormEvent<HTMLFormElement>) {
    e.preventDefault();
    if (newPw.length < 12 || newPw !== confirmPw) return;
    setPwState({ kind: 'submitting' });
    try {
      await changePassword(currentPw, newPw);
      setPwState({ kind: 'success' });
      setCurrentPw('');
      setNewPw('');
      setConfirmPw('');
    } catch (err) {
      setPwState({
        kind: 'error',
        message: err instanceof LoginFailedError
          ? err.message
          : t('Đổi mật khẩu thất bại.', 'Password change failed.'),
      });
    }
  }

  return (
    <div style={{ padding: 'var(--s-6)', display: 'flex', flexDirection: 'column', gap: 'var(--s-6)', maxWidth: 720 }}>
      <h1 className="t-xl" style={{ margin: 0, fontWeight: 600 }}>
        {t('Bảo mật tài khoản', 'Account security')}
      </h1>

      {/* MFA card */}
      <section className="card" style={{ padding: 'var(--s-5)', display: 'flex', flexDirection: 'column', gap: 'var(--s-3)' }}>
        <h2 className="t-lg" style={{ margin: 0 }}>{t('Xác thực 2 yếu tố', 'Two-factor authentication')}</h2>
        <p className="t-sm" style={{ margin: 0, color: 'var(--ink-2)' }}>
          {mfaEnrolled
            ? t('Đã kích hoạt cho tài khoản của bạn.', 'Enabled on your account.')
            : t('Chưa kích hoạt.', 'Not enabled.')}
        </p>
        {mfaEnrolled ? (
          <Button type="button" variant="secondary" size="md" onClick={() => setDisableModalOpen(true)}>
            {t('Tắt MFA', 'Disable MFA')}
          </Button>
        ) : (
          <Button type="button" variant="primary" size="md" onClick={onMfaResetRequest}>
            {t('Bật MFA', 'Enable MFA')}
          </Button>
        )}
      </section>

      {/* Recovery codes card */}
      {mfaEnrolled && (
        <section className="card" style={{ padding: 'var(--s-5)', display: 'flex', flexDirection: 'column', gap: 'var(--s-3)' }}>
          <h2 className="t-lg" style={{ margin: 0 }}>{t('Mã khôi phục', 'Recovery codes')}</h2>
          {!regeneratedCodes ? (
            <>
              <p className="t-sm" style={{ margin: 0, color: 'var(--ink-2)' }}>
                {t(
                  'Tạo lại bộ mã khôi phục mới. Bộ cũ sẽ bị vô hiệu hóa.',
                  'Generate a fresh set. The old codes are invalidated.',
                )}
              </p>
              <Button
                type="button"
                variant="secondary"
                size="md"
                onClick={handleRegenerate}
                disabled={regenLoading}
                aria-busy={regenLoading}
              >
                {regenLoading
                  ? t('Đang tạo…', 'Generating…')
                  : t('Tạo mã mới', 'Regenerate codes')}
              </Button>
            </>
          ) : (
            <RecoveryCodesDisplay
              codes={regeneratedCodes}
              onContinue={() => setRegeneratedCodes(null)}
            />
          )}
        </section>
      )}

      {/* Change password card */}
      <section className="card" style={{ padding: 'var(--s-5)', display: 'flex', flexDirection: 'column', gap: 'var(--s-3)' }}>
        <h2 className="t-lg" style={{ margin: 0 }}>{t('Đổi mật khẩu', 'Change password')}</h2>
        <form onSubmit={handleChangePassword} style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-3)' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-1)' }}>
            <label htmlFor="cur-pw" className="lbl">{t('Mật khẩu hiện tại', 'Current password')}</label>
            <input
              id="cur-pw"
              type="password"
              required
              autoComplete="current-password"
              value={currentPw}
              onChange={(e) => setCurrentPw(e.target.value)}
              disabled={pwState.kind === 'submitting'}
            />
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-1)' }}>
            <label htmlFor="new-pw" className="lbl">{t('Mật khẩu mới', 'New password')}</label>
            <input
              id="new-pw"
              type="password"
              required
              autoComplete="new-password"
              minLength={12}
              value={newPw}
              onChange={(e) => setNewPw(e.target.value)}
              disabled={pwState.kind === 'submitting'}
            />
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-1)' }}>
            <label htmlFor="confirm-pw" className="lbl">{t('Xác nhận', 'Confirm')}</label>
            <input
              id="confirm-pw"
              type="password"
              required
              autoComplete="new-password"
              value={confirmPw}
              onChange={(e) => setConfirmPw(e.target.value)}
              disabled={pwState.kind === 'submitting'}
            />
          </div>
          {pwState.kind === 'success' && (
            <div role="status" className="t-sm" style={{ color: 'var(--ok-fg)' }}>
              {t('Đã đổi mật khẩu.', 'Password changed.')}
            </div>
          )}
          {pwState.kind === 'error' && (
            <div role="alert" className="t-sm" style={{ color: '#7A1A1A' }}>
              {pwState.message}
            </div>
          )}
          <Button
            type="submit"
            variant="primary"
            size="md"
            disabled={
              currentPw.length === 0
              || newPw.length < 12
              || newPw !== confirmPw
              || pwState.kind === 'submitting'
            }
          >
            {pwState.kind === 'submitting'
              ? t('Đang lưu…', 'Saving…')
              : t('Đổi mật khẩu', 'Change password')}
          </Button>
        </form>
      </section>

      {/* Disable-MFA modal */}
      {disableModalOpen && (
        <div
          role="dialog"
          aria-modal="true"
          aria-label={t('Xác nhận tắt MFA', 'Confirm disable MFA')}
          style={{
            position: 'fixed',
            inset: 0,
            background: 'rgba(0,0,0,0.4)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            zIndex: 100,
          }}
        >
          <form
            onSubmit={handleDisableConfirm}
            className="card"
            style={{
              width: 400,
              padding: 'var(--s-6)',
              display: 'flex',
              flexDirection: 'column',
              gap: 'var(--s-3)',
            }}
          >
            <h2 className="t-lg" style={{ margin: 0 }}>{t('Tắt MFA?', 'Disable MFA?')}</h2>
            <p className="t-sm" style={{ margin: 0, color: 'var(--ink-2)' }}>
              {t('Nhập mật khẩu hiện tại để xác nhận.', 'Enter your current password to confirm.')}
            </p>
            <input
              type="password"
              required
              autoComplete="current-password"
              aria-label={t('Mật khẩu hiện tại', 'Current password')}
              value={disablePassword}
              onChange={(e) => setDisablePassword(e.target.value)}
              disabled={disableSubmitting}
            />
            {disableError && (
              <div role="alert" className="t-sm" style={{ color: '#7A1A1A' }}>
                {disableError}
              </div>
            )}
            <div style={{ display: 'flex', gap: 'var(--s-2)' }}>
              <Button
                type="button"
                variant="secondary"
                size="md"
                onClick={() => {
                  setDisableModalOpen(false);
                  setDisablePassword('');
                  setDisableError(null);
                }}
                disabled={disableSubmitting}
              >
                {t('Hủy', 'Cancel')}
              </Button>
              <Button
                type="submit"
                variant="primary"
                size="md"
                disabled={disablePassword.length === 0 || disableSubmitting}
              >
                {disableSubmitting
                  ? t('Đang tắt…', 'Disabling…')
                  : t('Xác nhận tắt', 'Confirm disable')}
              </Button>
            </div>
          </form>
        </div>
      )}

      {user && (
        <p className="t-xs" style={{ color: 'var(--ink-3)' }}>
          {user.email} · {user.role}
        </p>
      )}
    </div>
  );
}
