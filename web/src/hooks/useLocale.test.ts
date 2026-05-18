import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { useLocale, setLang, getLang, t, __resetLocaleForTests } from './useLocale';

describe('useLocale', () => {
  beforeEach(() => {
    __resetLocaleForTests();
  });

  afterEach(() => {
    __resetLocaleForTests();
  });

  it('defaults to vi when no localStorage value present', () => {
    expect(getLang()).toBe('vi');
  });

  it('returns vi string from t() when locale is vi', () => {
    expect(t('Tồn kho', 'Inventory')).toBe('Tồn kho');
  });

  it('returns en string from t() after setLang("en")', () => {
    setLang('en');
    expect(t('Tồn kho', 'Inventory')).toBe('Inventory');
  });

  it('falls back to vi when en argument is omitted', () => {
    setLang('en');
    expect(t('Vietnamese only')).toBe('Vietnamese only');
  });

  it('persists lang to localStorage on setLang', () => {
    setLang('en');
    expect(window.localStorage.getItem('shopflow.lang')).toBe('en');
  });

  it('writes document.documentElement.lang on setLang', () => {
    setLang('en');
    expect(document.documentElement.lang).toBe('en-US');
    setLang('vi');
    expect(document.documentElement.lang).toBe('vi-VN');
  });

  it('useLocale() returns current lang + setter', () => {
    const { result } = renderHook(() => useLocale());
    expect(result.current.lang).toBe('vi');

    act(() => {
      result.current.setLang('en');
    });

    expect(result.current.lang).toBe('en');
  });

  it('re-renders subscribed components when lang flips', () => {
    let renders = 0;
    const { result } = renderHook(() => {
      renders += 1;
      return useLocale();
    });

    const initialRenders = renders;

    act(() => {
      result.current.setLang('en');
    });

    expect(renders).toBeGreaterThan(initialRenders);
  });

  it('setLang is a no-op when value unchanged', () => {
    let renders = 0;
    renderHook(() => {
      renders += 1;
      return useLocale();
    });

    const initialRenders = renders;

    act(() => {
      setLang('vi'); // already vi
    });

    expect(renders).toBe(initialRenders);
  });
});
