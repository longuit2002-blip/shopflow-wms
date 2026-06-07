/**
 * MarkPickFailedModal tests — Sprint-11 plan U2.
 *
 * Pins the Sprint-6 KTD9 Modal-primitive contract for the picker reason-
 * capture surface:
 *   1. Renders when isOpen=true (and is null when isOpen=false).
 *   2. Submit button disabled when reason is empty / whitespace only.
 *   3. Submit button enabled once reason has non-whitespace content.
 *   4. Submit fires onSubmit with the trimmed reason.
 *   5. Cancel fires onClose.
 *   6. isPending=true disables both buttons + sets aria-busy on submit.
 *   7. Esc closes via Modal's capture-phase listener.
 */

import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { render, screen, fireEvent } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MarkPickFailedModal } from './MarkPickFailedModal';
import { __resetLocaleForTests } from '../../hooks/useLocale';

beforeEach(() => __resetLocaleForTests());
afterEach(() => __resetLocaleForTests());

describe('MarkPickFailedModal (Sprint-11 U2)', () => {
  it('renders nothing when isOpen=false', () => {
    render(
      <MarkPickFailedModal
        isOpen={false}
        onClose={vi.fn()}
        onSubmit={vi.fn()}
        isPending={false}
      />,
    );
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument();
  });

  it('renders dialog + textarea when isOpen=true', () => {
    render(
      <MarkPickFailedModal
        isOpen={true}
        onClose={vi.fn()}
        onSubmit={vi.fn()}
        isPending={false}
      />,
    );
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(screen.getByTestId('mark-pick-failed-reason')).toBeInTheDocument();
  });

  it('submit button disabled when reason is empty', () => {
    render(
      <MarkPickFailedModal
        isOpen={true}
        onClose={vi.fn()}
        onSubmit={vi.fn()}
        isPending={false}
      />,
    );
    expect(screen.getByTestId('mark-pick-failed-submit')).toBeDisabled();
  });

  it('submit button disabled when reason is whitespace only', async () => {
    const user = userEvent.setup();
    render(
      <MarkPickFailedModal
        isOpen={true}
        onClose={vi.fn()}
        onSubmit={vi.fn()}
        isPending={false}
      />,
    );
    await user.type(screen.getByTestId('mark-pick-failed-reason'), '   ');
    expect(screen.getByTestId('mark-pick-failed-submit')).toBeDisabled();
  });

  it('submit button enabled once reason has non-whitespace content', async () => {
    const user = userEvent.setup();
    render(
      <MarkPickFailedModal
        isOpen={true}
        onClose={vi.fn()}
        onSubmit={vi.fn()}
        isPending={false}
      />,
    );
    await user.type(
      screen.getByTestId('mark-pick-failed-reason'),
      'Out of stock on shelf',
    );
    expect(screen.getByTestId('mark-pick-failed-submit')).toBeEnabled();
  });

  it('submit fires onSubmit with the trimmed reason', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn();
    render(
      <MarkPickFailedModal
        isOpen={true}
        onClose={vi.fn()}
        onSubmit={onSubmit}
        isPending={false}
      />,
    );
    await user.type(
      screen.getByTestId('mark-pick-failed-reason'),
      '  Damaged box  ',
    );
    await user.click(screen.getByTestId('mark-pick-failed-submit'));
    expect(onSubmit).toHaveBeenCalledTimes(1);
    expect(onSubmit).toHaveBeenCalledWith('Damaged box');
  });

  it('cancel fires onClose without firing onSubmit', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    const onSubmit = vi.fn();
    render(
      <MarkPickFailedModal
        isOpen={true}
        onClose={onClose}
        onSubmit={onSubmit}
        isPending={false}
      />,
    );
    await user.click(screen.getByTestId('mark-pick-failed-cancel'));
    expect(onClose).toHaveBeenCalledTimes(1);
    expect(onSubmit).not.toHaveBeenCalled();
  });

  it('isPending disables both buttons + sets aria-busy on submit', async () => {
    const user = userEvent.setup();
    render(
      <MarkPickFailedModal
        isOpen={true}
        onClose={vi.fn()}
        onSubmit={vi.fn()}
        isPending={true}
      />,
    );
    // Populate reason first so submit's "empty" disable doesn't mask the
    // pending-driven disable.
    await user.type(
      screen.getByTestId('mark-pick-failed-reason'),
      'Reason here',
    );
    const submit = screen.getByTestId('mark-pick-failed-submit');
    expect(submit).toBeDisabled();
    expect(submit.getAttribute('aria-busy')).toBe('true');
    expect(screen.getByTestId('mark-pick-failed-cancel')).toBeDisabled();
  });

  it('isPending blocks onClose via the Cancel button', async () => {
    const user = userEvent.setup();
    const onClose = vi.fn();
    render(
      <MarkPickFailedModal
        isOpen={true}
        onClose={onClose}
        onSubmit={vi.fn()}
        isPending={true}
      />,
    );
    await user.click(screen.getByTestId('mark-pick-failed-cancel'));
    expect(onClose).not.toHaveBeenCalled();
  });

  it('Esc closes via Modal capture-phase listener', () => {
    const onClose = vi.fn();
    render(
      <MarkPickFailedModal
        isOpen={true}
        onClose={onClose}
        onSubmit={vi.fn()}
        isPending={false}
      />,
    );
    fireEvent.keyDown(document, { key: 'Escape' });
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('reason field resets each time the modal re-opens', () => {
    const { rerender } = render(
      <MarkPickFailedModal
        isOpen={true}
        onClose={vi.fn()}
        onSubmit={vi.fn()}
        isPending={false}
      />,
    );
    const ta = screen.getByTestId('mark-pick-failed-reason') as HTMLTextAreaElement;
    fireEvent.change(ta, { target: { value: 'First reason' } });
    expect(ta.value).toBe('First reason');

    // Close + reopen.
    rerender(
      <MarkPickFailedModal
        isOpen={false}
        onClose={vi.fn()}
        onSubmit={vi.fn()}
        isPending={false}
      />,
    );
    rerender(
      <MarkPickFailedModal
        isOpen={true}
        onClose={vi.fn()}
        onSubmit={vi.fn()}
        isPending={false}
      />,
    );
    const taAfter = screen.getByTestId('mark-pick-failed-reason') as HTMLTextAreaElement;
    expect(taAfter.value).toBe('');
  });
});
