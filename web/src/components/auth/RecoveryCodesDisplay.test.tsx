import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { RecoveryCodesDisplay } from './RecoveryCodesDisplay';
import { __resetLocaleForTests } from '../../hooks/useLocale';

const CODES = [
  'abcd-1234',
  'efgh-5678',
  'ijkl-9012',
  'mnop-3456',
  'qrst-7890',
  'uvwx-1357',
  'yzab-2468',
  'cdef-1928',
  'ghij-3847',
  'klmn-5766',
];

describe('RecoveryCodesDisplay (Sprint-9.5 U6)', () => {
  beforeEach(() => __resetLocaleForTests());
  afterEach(() => __resetLocaleForTests());

  it('renders all 10 codes in a list', () => {
    const onContinue = vi.fn();
    render(<RecoveryCodesDisplay codes={CODES} onContinue={onContinue} />);

    for (const code of CODES) {
      expect(screen.getByText(code)).toBeInTheDocument();
    }
  });

  it('Continue button is disabled until the ack checkbox is checked', async () => {
    const user = userEvent.setup();
    const onContinue = vi.fn();
    render(<RecoveryCodesDisplay codes={CODES} onContinue={onContinue} />);

    const continueButton = screen.getByRole('button', { name: /Tiếp tục|Continue/i });
    expect(continueButton).toBeDisabled();

    await user.click(screen.getByRole('checkbox'));
    expect(continueButton).toBeEnabled();
  });

  it('fires onContinue only after the ack box is checked + Continue click', async () => {
    const user = userEvent.setup();
    const onContinue = vi.fn();
    render(<RecoveryCodesDisplay codes={CODES} onContinue={onContinue} />);

    await user.click(screen.getByRole('checkbox'));
    await user.click(screen.getByRole('button', { name: /Tiếp tục|Continue/i }));
    expect(onContinue).toHaveBeenCalledTimes(1);
  });

  it('Download as .txt creates and revokes a blob URL', async () => {
    const user = userEvent.setup();
    const onContinue = vi.fn();
    const createUrl = vi.fn(() => 'blob:fake-url');
    const revokeUrl = vi.fn();
    // jsdom provides URL but not these methods on the global by default in some setups.
    URL.createObjectURL = createUrl;
    URL.revokeObjectURL = revokeUrl;

    render(<RecoveryCodesDisplay codes={CODES} onContinue={onContinue} />);
    await user.click(
      screen.getByRole('button', { name: /Tải xuống dưới dạng \.txt|Download as \.txt/i }),
    );

    expect(createUrl).toHaveBeenCalledTimes(1);
    expect(revokeUrl).toHaveBeenCalledWith('blob:fake-url');
  });
});
