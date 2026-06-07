import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { MfaEnrollScreen } from './MfaEnrollScreen';
import { useAuth, __resetAuthForTests } from '../../hooks/useAuth';
import { __resetLocaleForTests } from '../../hooks/useLocale';

const VALID_JWT =
  'eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9'
  + '.eyJzdWIiOiI4ZjcyZjUxNi1jYzAyLTRmNTQtOWNjOC1iZTBmM2I5NmM0ZjMiLCJlbWFpbCI6Im93bmVyQHllbnNhby52biIsInJvbGUiOiJPd25lciIsInRlbmFudF9zbHVnIjoieWVuc2Fva2hhbmhob2EiLCJleHAiOjk5OTk5OTk5OTl9'
  + '.signature';

function beginEnrollResponse() {
  return new Response(
    JSON.stringify({
      qrSvg: '<svg xmlns="http://www.w3.org/2000/svg" data-testid="qr-svg"><rect width="100" height="100"/></svg>',
      manualSecret: 'JBSWY3DPEHPK3PXP',
      enrollmentId: 'enrollment-uuid-1',
    }),
    { status: 200, headers: { 'Content-Type': 'application/json' } },
  );
}

function verifyEnrollResponse() {
  return new Response(
    JSON.stringify({
      accessToken: VALID_JWT,
      accessTokenExpiresAt: new Date(Date.now() + 15 * 60 * 1000).toISOString(),
      refreshToken: 'opaque-refresh',
      refreshTokenExpiresAt: new Date(Date.now() + 7 * 24 * 60 * 60 * 1000).toISOString(),
      role: 'Owner',
      email: 'owner@yensao.vn',
      recoveryCodes: [
        'aaaa-1111', 'bbbb-2222', 'cccc-3333', 'dddd-4444', 'eeee-5555',
        'ffff-6666', 'gggg-7777', 'hhhh-8888', 'iiii-9999', 'jjjj-0000',
      ],
    }),
    { status: 200, headers: { 'Content-Type': 'application/json' } },
  );
}

describe('MfaEnrollScreen (Sprint-9.5 U6 — F2 enrollment)', () => {
  beforeEach(() => {
    __resetAuthForTests();
    __resetLocaleForTests();
    vi.stubGlobal('fetch', vi.fn());
    URL.createObjectURL = vi.fn(() => 'blob:fake');
    URL.revokeObjectURL = vi.fn();
    useAuth.getState().setMfaEnrollment('intent-xyz');
  });
  afterEach(() => {
    __resetAuthForTests();
    __resetLocaleForTests();
    vi.unstubAllGlobals();
  });

  it('Step 1: fetches the QR + renders SVG + manual secret', async () => {
    vi.mocked(globalThis.fetch).mockResolvedValueOnce(beginEnrollResponse());
    render(<MfaEnrollScreen />);

    await waitFor(() => {
      expect(screen.getByTestId('enrollment-qr')).toBeInTheDocument();
    });
    expect(screen.getByText('JBSWY3DPEHPK3PXP')).toBeInTheDocument();
  });

  it('Step 2: Verify button disabled until 6-digit input', async () => {
    const user = userEvent.setup();
    vi.mocked(globalThis.fetch).mockResolvedValueOnce(beginEnrollResponse());
    render(<MfaEnrollScreen />);

    await screen.findByTestId('enrollment-qr');

    const button = screen.getByRole('button', { name: /^Verify$|^Xác minh$/i });
    expect(button).toBeDisabled();

    await user.type(screen.getByLabelText(/6-digit code|Mã 6 chữ số/i), '12345');
    expect(button).toBeDisabled();

    await user.type(screen.getByLabelText(/6-digit code|Mã 6 chữ số/i), '6');
    expect(button).toBeEnabled();
  });

  it('Step 3: post-verify renders RecoveryCodesDisplay with 10 codes; Continue disabled until ack', async () => {
    const user = userEvent.setup();
    vi.mocked(globalThis.fetch)
      .mockResolvedValueOnce(beginEnrollResponse())
      .mockResolvedValueOnce(verifyEnrollResponse());
    render(<MfaEnrollScreen />);

    await screen.findByTestId('enrollment-qr');

    await user.type(screen.getByLabelText(/6-digit code|Mã 6 chữ số/i), '123456');
    await user.click(screen.getByRole('button', { name: /^Verify$|^Xác minh$/i }));

    await waitFor(() => {
      expect(screen.getByTestId('recovery-codes-display')).toBeInTheDocument();
    });
    expect(screen.getByText('aaaa-1111')).toBeInTheDocument();
    expect(screen.getByText('jjjj-0000')).toBeInTheDocument();

    const continueButton = screen.getByRole('button', { name: /^Continue$|^Tiếp tục$/i });
    expect(continueButton).toBeDisabled();
  });

  it('full flow: enroll → verify → ack → Continue promotes useAuth to full-session', async () => {
    const user = userEvent.setup();
    vi.mocked(globalThis.fetch)
      .mockResolvedValueOnce(beginEnrollResponse())
      .mockResolvedValueOnce(verifyEnrollResponse());
    const onEnrollmentComplete = vi.fn();
    render(<MfaEnrollScreen onEnrollmentComplete={onEnrollmentComplete} />);

    await screen.findByTestId('enrollment-qr');
    await user.type(screen.getByLabelText(/6-digit code|Mã 6 chữ số/i), '123456');
    await user.click(screen.getByRole('button', { name: /^Verify$|^Xác minh$/i }));

    await screen.findByTestId('recovery-codes-display');
    await user.click(screen.getByRole('checkbox'));
    await user.click(screen.getByRole('button', { name: /^Continue$|^Tiếp tục$/i }));

    expect(onEnrollmentComplete).toHaveBeenCalledTimes(1);
    expect(useAuth.getState().authState).toBe('full-session');
    expect(useAuth.getState().isAuthenticated).toBe(true);
  });
});
