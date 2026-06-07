import { useState } from 'react';
import { Button } from '../primitives/Button';
import { t, useLocale } from '../../hooks/useLocale';

/**
 * Sprint-9.5 U6 — single-display surface for the 10 recovery codes the
 * backend emits on MFA enrollment + regeneration. OWASP guidance: codes
 * shown ONCE; the user MUST acknowledge they've saved them before the
 * Continue button enables.
 *
 * "Download as .txt" produces a Blob URL via createObjectURL +
 * revokeObjectURL in the click handler's finally so the codes don't
 * remain reachable for the tab lifetime (Sprint-7 KTD9 + Sprint-9.5
 * KTD8 spirit).
 */
export interface RecoveryCodesDisplayProps {
  codes: readonly string[];
  /** Fires after the user checks the ack box and clicks Continue. */
  onContinue: () => void;
  /** Optional caller-supplied filename for the .txt download. */
  filename?: string;
}

export function RecoveryCodesDisplay({
  codes,
  onContinue,
  filename = 'shopflow-recovery-codes.txt',
}: RecoveryCodesDisplayProps) {
  useLocale();
  const [acknowledged, setAcknowledged] = useState(false);

  function handleDownload() {
    const header = '# ShopFlow recovery codes — keep these safe.';
    const body = codes.join('\n');
    const text = `${header}\n\n${body}\n`;
    const blob = new Blob([text], { type: 'text/plain' });
    const url = URL.createObjectURL(blob);
    try {
      const link = document.createElement('a');
      link.href = url;
      link.download = filename;
      document.body.appendChild(link);
      link.click();
      document.body.removeChild(link);
    } finally {
      URL.revokeObjectURL(url);
    }
  }

  return (
    <div
      data-testid="recovery-codes-display"
      style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-4)' }}
    >
      <div>
        <h2 className="t-lg" style={{ margin: 0, fontWeight: 600 }}>
          {t('Lưu mã khôi phục', 'Save your recovery codes')}
        </h2>
        <p className="t-sm" style={{ margin: '4px 0 0', color: 'var(--ink-2)' }}>
          {t(
            'Chỉ hiển thị một lần. Mỗi mã chỉ dùng được một lần để đăng nhập nếu mất ứng dụng xác thực.',
            'Shown once. Each code is single-use and gets you back in if you lose your authenticator.',
          )}
        </p>
      </div>

      <ul
        aria-label={t('Mã khôi phục', 'Recovery codes')}
        style={{
          listStyle: 'none',
          margin: 0,
          padding: 'var(--s-3)',
          background: 'var(--bg-soft)',
          borderRadius: 'var(--radius-md)',
          display: 'grid',
          gridTemplateColumns: '1fr 1fr',
          gap: 'var(--s-2)',
          fontFamily: 'monospace',
          fontSize: 14,
        }}
      >
        {codes.map((code, index) => (
          <li key={code}>
            <span style={{ color: 'var(--ink-3)', marginRight: 8 }}>{index + 1}.</span>
            <code>{code}</code>
          </li>
        ))}
      </ul>

      <Button type="button" variant="secondary" size="md" onClick={handleDownload}>
        {t('Tải xuống dưới dạng .txt', 'Download as .txt')}
      </Button>

      <label
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 'var(--s-2)',
          fontSize: 14,
          color: 'var(--ink-2)',
        }}
      >
        <input
          type="checkbox"
          checked={acknowledged}
          onChange={(e) => setAcknowledged(e.target.checked)}
        />
        {t(
          'Tôi đã lưu mã khôi phục ở nơi an toàn',
          "I've saved my recovery codes in a safe place",
        )}
      </label>

      <Button
        type="button"
        variant="primary"
        size="lg"
        disabled={!acknowledged}
        onClick={onContinue}
      >
        {t('Tiếp tục', 'Continue')}
      </Button>
    </div>
  );
}
