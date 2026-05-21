import { t, useLocale } from '../../hooks/useLocale';

export type MfaStatus = 'enrolled' | 'required-not-enrolled' | 'not-enrolled';

export function deriveMfaStatus(args: {
  mfaEnrolled: boolean;
  mfaRequired: boolean;
}): MfaStatus {
  if (args.mfaEnrolled) return 'enrolled';
  if (args.mfaRequired) return 'required-not-enrolled';
  return 'not-enrolled';
}

export interface MfaStatusBadgeProps {
  status: MfaStatus;
}

/**
 * Sprint-9.5 U7 — visual badge for the `/admin/users` table's MFA
 * column. Three variants derived from `(mfaEnrolled, mfaRequired)` —
 * Owner rows surface "Required, not enrolled" in red so an unmet
 * forced-enrollment is operationally visible.
 */
export function MfaStatusBadge({ status }: MfaStatusBadgeProps) {
  useLocale();
  const config: Record<MfaStatus, { label: string; bg: string; fg: string }> = {
    enrolled: {
      label: t('Đã kích hoạt', 'Enrolled'),
      bg: 'var(--ok-100)',
      fg: '#1B5E20',
    },
    'required-not-enrolled': {
      label: t('Bắt buộc, chưa kích hoạt', 'Required, not enrolled'),
      bg: 'var(--danger-100)',
      fg: '#7A1A1A',
    },
    'not-enrolled': {
      label: t('Chưa kích hoạt', 'Not enrolled'),
      bg: 'var(--bg-soft)',
      fg: 'var(--ink-2)',
    },
  };
  const cfg = config[status];
  return (
    <span
      data-status={status}
      style={{
        display: 'inline-block',
        padding: '2px 8px',
        borderRadius: 12,
        background: cfg.bg,
        color: cfg.fg,
        fontSize: 12,
        fontWeight: 500,
      }}
    >
      {cfg.label}
    </span>
  );
}
