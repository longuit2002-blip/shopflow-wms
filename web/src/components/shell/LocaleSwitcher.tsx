/**
 * LocaleSwitcher — segmented VI / EN toggle.
 *
 * Ports the design canon `app.jsx` language toggle (~line 199): two
 * mono-uppercase pills inside a 1 px bordered tray. The active button
 * inverts to ink-on-ink-inv; the inactive stays muted on the bg-soft tray.
 *
 * On click, `useLocale().setLang` flips current locale; `<html lang>` and
 * localStorage updates happen inside the hook (STYLING_SPECS §6.4 +
 * persistence requirement). Subscribed components re-render.
 */

import { useLocale, type LocaleCode } from '../../hooks/useLocale';

const OPTIONS: ReadonlyArray<{ id: LocaleCode; label: string }> = [
  { id: 'vi', label: 'VI' },
  { id: 'en', label: 'EN' },
];

export function LocaleSwitcher() {
  const { lang, setLang } = useLocale();

  return (
    <div
      role="group"
      aria-label="Locale switcher"
      style={{
        display: 'flex',
        alignItems: 'center',
        padding: '2px',
        border: '1px solid var(--line)',
        borderRadius: 'var(--radius-md)',
        background: 'var(--bg-soft)',
      }}
    >
      {OPTIONS.map((opt) => {
        const active = lang === opt.id;
        return (
          <button
            key={opt.id}
            type="button"
            onClick={() => setLang(opt.id)}
            aria-pressed={active}
            aria-label={opt.id === 'vi' ? 'Tiếng Việt' : 'English'}
            style={{
              padding: '3px 8px',
              height: 24,
              border: 'none',
              borderRadius: 3,
              background: active ? 'var(--ink)' : 'transparent',
              color: active ? 'var(--ink-inv)' : 'var(--ink-2)',
              fontFamily: 'var(--font-mono)',
              fontSize: 10.5,
              fontWeight: 600,
              cursor: 'pointer',
              letterSpacing: '0.04em',
            }}
          >
            {opt.label}
          </button>
        );
      })}
    </div>
  );
}
