/**
 * Locale store + inline translator.
 *
 * Mirrors the prototype `i18n.jsx` shape literally: `t(vi, en)` takes two
 * inline strings and returns the one matching current locale. Vietnamese
 * is the default; English falls back to Vietnamese if not provided.
 *
 * Why inline tuples instead of keyed dictionaries:
 * - The design canon ships strings inline next to components. Porting to
 *   keys would require inventing names for ~200+ short labels and risk
 *   drift between dictionary + usage site.
 * - Sprint-6 ships 1 real screen (Inventory) + 8 ComingSoon stubs. The
 *   string surface is small enough that named keys add ceremony without
 *   payoff.
 * - Sprint-7+ can swap in i18next or formatjs without changing call sites
 *   if the maintenance burden grows.
 *
 * Persistence: `localStorage.shopflow.lang` ('vi' | 'en'). On `setLang`,
 * also writes `document.documentElement.lang` to 'vi-VN' or 'en-US' for
 * screen readers + STYLING_SPECS §6.4 honored.
 */

import { useSyncExternalStore } from 'react';

export type LocaleCode = 'vi' | 'en';

const STORAGE_KEY = 'shopflow.lang';
const HTML_LANG: Record<LocaleCode, string> = {
  vi: 'vi-VN',
  en: 'en-US',
};

const isLocale = (v: unknown): v is LocaleCode => v === 'vi' || v === 'en';

function readStored(): LocaleCode {
  if (typeof window === 'undefined') return 'vi';
  try {
    const raw = window.localStorage.getItem(STORAGE_KEY);
    return isLocale(raw) ? raw : 'vi';
  } catch {
    return 'vi';
  }
}

function writeStored(code: LocaleCode): void {
  if (typeof window === 'undefined') return;
  try {
    window.localStorage.setItem(STORAGE_KEY, code);
  } catch {
    // localStorage may be disabled (private mode); honor the in-memory value silently
  }
}

function syncHtmlLang(code: LocaleCode): void {
  if (typeof document === 'undefined') return;
  document.documentElement.lang = HTML_LANG[code];
}

// In-memory state + subscriber set, used by useSyncExternalStore so React
// re-renders every component that called useLocale() when setLang flips.
let currentLang: LocaleCode = readStored();
const listeners = new Set<() => void>();

function subscribe(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

function getSnapshot(): LocaleCode {
  return currentLang;
}

export function setLang(code: LocaleCode): void {
  if (currentLang === code) return;
  currentLang = code;
  writeStored(code);
  syncHtmlLang(code);
  listeners.forEach((l) => l());
}

export function getLang(): LocaleCode {
  return currentLang;
}

/**
 * Inline translator. Pass both strings; returns the one matching the
 * current locale (en falls back to vi if `en` is omitted).
 *
 * NOTE: Not a React hook — safe to call from non-component code, but the
 * component will only re-render on locale change if it also calls
 * useLocale() (which triggers a subscription).
 */
export function t(vi: string, en?: string): string {
  return currentLang === 'en' && en != null ? en : vi;
}

export interface LocaleState {
  lang: LocaleCode;
  setLang: (code: LocaleCode) => void;
}

/**
 * React hook — subscribes the calling component to locale changes.
 * Returns the current `lang` + a stable `setLang` setter.
 */
export function useLocale(): LocaleState {
  const lang = useSyncExternalStore(subscribe, getSnapshot, getSnapshot);
  return { lang, setLang };
}

// On module load, sync the <html lang> attribute to whatever localStorage
// holds (the index.html ships with lang="vi-VN", so this is a no-op for
// new users but corrects drift for returning English-locale users).
if (typeof document !== 'undefined') {
  syncHtmlLang(currentLang);
}

// Test-only reset hook. Vitest's `cleanup` runs after each test, but the
// module-level singleton state outlives test isolation. Exposing this
// makes the test suite deterministic without changing prod call sites.
export function __resetLocaleForTests(): void {
  currentLang = 'vi';
  listeners.clear();
  try {
    window.localStorage.removeItem(STORAGE_KEY);
  } catch {
    // ignore
  }
  syncHtmlLang('vi');
}
