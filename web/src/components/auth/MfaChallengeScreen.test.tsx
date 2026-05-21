import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { MfaChallengeScreen } from './MfaChallengeScreen';
import { useAuth, __resetAuthForTests } from '../../hooks/useAuth';
import { __resetLocaleForTests } from '../../hooks/useLocale';

// JWT carrying a valid Sprint-9 claim set + far-future exp.
const VALID_JWT =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9'
  + '.eyJzdWIiOiI4ZjcyZjUxNi1jYzAyLTRmNTQtOWNjOC1iZTBmM2I5NmM0ZjMiLCJlbWFpbCI6Im93bmVyQHllbnNhby52biIsInJvbGUiOiJPd25lciIsInRlbmFudF9zbHVnIjoieWVuc2Fva2hhbmhob2EiLCJleHAiOjk5OTk5OTk5OTl9'
  + '.signature';

function mfaVerifyOkResponse() {
  return new Response(
    JSON.stringify({
      accessToken: VALID_JWT,
      accessTokenExpiresAt: new Date(Date.now() + 15 * 60 * 1000).toISOString(),
      refreshToken: 'opaque-refresh',
      refreshTokenExpiresAt: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(),
      role: 'Owner',
      email: 'owner@yensao.vn',
    }),
    { status: 200, headers: { 'Content-Type': 'application/json' } },
  );
}

function mfaVerifyFailResponse() {
  return new Response(
    JSON.stringify({ title: 'Invalid', detail: 'Wrong code', error_code: 'auth.mfa_invalid' }),
    { status: 401, headers: { 'Content-Type': 'application/json' } },
  );
}

describe('MfaChallengeScreen (Sprint-9.5 U6 — F3 challenge)', () => {
  beforeEach(() => {
    __resetAuthForTests();
    __resetLocaleForTests();
    vi.stubGlobal('fetch', vi.fn());
    // Pre-populate intent token (the route guard normally ensures this).
    useAuth.getState().setMfaChallenge('intent-abc', ['totp', 'recovery']);
  });
  afterEach(() => {
    __resetAuthForTests();
    __resetLocaleForTests();
    vi.unstubAllGlobals();
  });

  it('renders OTP input with autoFocus + numeric inputMode', () => {
    render(<MfaChallengeScreen />);
    const input = screen.getByLabelText(/6-digit code|Mã 6 chữ số/i) as HTMLInputElement;
    expect(input).toBeInTheDocument();
    expect(input.inputMode).toBe('numeric');
    expect(input).toHaveFocus();
  });

  it('"Use recovery code instead" toggles the input length expectation', async () => {
    const user = userEvent.setup();
    render(<MfaChallengeScreen />);
    await user.click(
      screen.getByRole('button', {
        name: /Use a recovery code instead|Dùng mã khôi phục/i,
      }),
    );
    const input = screen.getByLabelText(/Recovery code|Mã khôi phục/i) as HTMLInputElement;
    expect(input).toBeInTheDocument();
    expect(input.inputMode).toBe('text');
    expect(input.maxLength).toBe(8);
  });

  it('verify success → useAuth.setSession + onSuccess fires', async () => {
    const user = userEvent.setup();
    vi.mocked(globalThis.fetch).mockResolvedValue(mfaVerifyOkResponse());
    const onSuccess = vi.fn();
    render(<MfaChallengeScreen onSuccess={onSuccess} />);

    await user.type(screen.getByLabelText(/6-digit code|Mã 6 chữ số/i), '123456');
    await user.click(screen.getByRole('button', { name: /^Verify$|^Xác minh$/i }));

    await waitFor(() => expect(onSuccess).toHaveBeenCalledTimes(1));
    expect(useAuth.getState().isAuthenticated).toBe(true);
    expect(useAuth.getState().authState).toBe('full-session');
  });

  it('3 bad OTPs → clearIntent + onSessionExpired fires', async () => {
    const user = userEvent.setup();
    vi.mocked(globalThis.fetch).mockResolvedValue(mfaVerifyFailResponse());
    const onSessionExpired = vi.fn();
    render(<MfaChallengeScreen onSessionExpired={onSessionExpired} />);

    for (let i = 0; i < 3; i++) {
      await user.clear(screen.getByLabelText(/6-digit code|Mã 6 chữ số/i));
      await user.type(screen.getByLabelText(/6-digit code|Mã 6 chữ số/i), '999999');
      await user.click(screen.getByRole('button', { name: /^Verify$|^Xác minh$/i }));
      // Yield to the test loop so re-renders settle.
      await waitFor(() => {
        // After attempt 1+2: error alert shows; after attempt 3: form unmounts.
        // We just need at least the fetch call to register.
        expect(vi.mocked(globalThis.fetch).mock.calls.length).toBeGreaterThan(i);
      });
    }

    await waitFor(() => expect(onSessionExpired).toHaveBeenCalledTimes(1));
    expect(useAuth.getState().authState).toBe('signed-out');
    expect(useAuth.getState().intentToken).toBeNull();
  });
});
