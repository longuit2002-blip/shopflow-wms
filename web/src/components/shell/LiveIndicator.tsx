/**
 * LiveIndicator — pulsing dot + "Live" / "Trực tiếp" label.
 *
 * Sprint-6 placeholder: status is hardcoded to "connected" because
 * SignalR is deferred to Sprint-7. The dot uses the canon `.live-dot`
 * animation (1.5 s opacity pulse) and the only motion the UI ships.
 *
 * `status` is exposed so Sprint-7 can flip the dot color without a
 * shape change. `prefers-reduced-motion` users see a static dot via the
 * tokens.css media query.
 */

import { t } from '../../hooks/useLocale';

export type LiveStatus = 'ok' | 'warn' | 'bad' | 'info';

export interface LiveIndicatorProps {
  status?: LiveStatus;
}

export function LiveIndicator({ status = 'info' }: LiveIndicatorProps) {
  return (
    <div
      data-live-indicator
      data-tour="live-indicator"
      className="fs0 nb"
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 6,
        padding: '0 8px',
        borderLeft: '1px solid var(--line)',
        height: 28,
      }}
    >
      <span className={`live-dot ${status === 'info' ? '' : status}`} />
      <span style={{ fontSize: 11, color: 'var(--ink-3)' }}>{t('Trực tiếp', 'Live')}</span>
    </div>
  );
}
