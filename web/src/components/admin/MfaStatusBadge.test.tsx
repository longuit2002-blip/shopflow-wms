import { render, screen } from '@testing-library/react';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { MfaStatusBadge, deriveMfaStatus } from './MfaStatusBadge';
import { __resetLocaleForTests } from '../../hooks/useLocale';

describe('MfaStatusBadge + deriveMfaStatus', () => {
  beforeEach(() => __resetLocaleForTests());
  afterEach(() => __resetLocaleForTests());

  it('deriveMfaStatus returns enrolled when mfaEnrolled=true', () => {
    expect(deriveMfaStatus({ mfaEnrolled: true, mfaRequired: false })).toBe('enrolled');
    expect(deriveMfaStatus({ mfaEnrolled: true, mfaRequired: true })).toBe('enrolled');
  });

  it('deriveMfaStatus returns required-not-enrolled when forced but absent', () => {
    expect(deriveMfaStatus({ mfaEnrolled: false, mfaRequired: true })).toBe(
      'required-not-enrolled',
    );
  });

  it('deriveMfaStatus returns not-enrolled otherwise', () => {
    expect(deriveMfaStatus({ mfaEnrolled: false, mfaRequired: false })).toBe('not-enrolled');
  });

  it('renders the three labels for each status', () => {
    const { rerender } = render(<MfaStatusBadge status="enrolled" />);
    expect(screen.getByText(/Enrolled|Đã kích hoạt/i)).toBeInTheDocument();
    rerender(<MfaStatusBadge status="required-not-enrolled" />);
    expect(screen.getByText(/Required, not enrolled|Bắt buộc/i)).toBeInTheDocument();
    rerender(<MfaStatusBadge status="not-enrolled" />);
    expect(screen.getByText(/Not enrolled|Chưa kích hoạt/i)).toBeInTheDocument();
  });

  it('emits data-status attribute for downstream selectors', () => {
    const { container } = render(<MfaStatusBadge status="required-not-enrolled" />);
    expect(container.querySelector('[data-status="required-not-enrolled"]')).toBeInTheDocument();
  });
});
