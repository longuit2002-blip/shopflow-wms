import { describe, it, expect, afterEach, beforeEach } from 'vitest';
import { render, screen, act } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ToastViewport } from './Toast';
import { useToast, __resetToastsForTests } from '../../hooks/useToast';
import { __resetLocaleForTests, setLang } from '../../hooks/useLocale';

beforeEach(() => {
  __resetToastsForTests();
});

afterEach(() => {
  __resetLocaleForTests();
  __resetToastsForTests();
});

describe('ToastViewport', () => {
  it('renders nothing when the queue is empty', () => {
    const { container } = render(<ToastViewport />);
    expect(container).toBeEmptyDOMElement();
  });

  it('renders a success toast with role=status', () => {
    render(<ToastViewport />);
    act(() => {
      useToast.getState().push({ kind: 'success', title: 'Đã lưu', durationMs: 0 });
    });
    expect(screen.getByTestId('toast-success')).toHaveAttribute('role', 'status');
    expect(screen.getByText('Đã lưu')).toBeInTheDocument();
  });

  it('renders an error toast with role=alert', () => {
    render(<ToastViewport />);
    act(() => {
      useToast.getState().push({ kind: 'error', title: 'Lỗi mạng', durationMs: 0 });
    });
    expect(screen.getByTestId('toast-error')).toHaveAttribute('role', 'alert');
  });

  it('shows the body line when provided', () => {
    render(<ToastViewport />);
    act(() => {
      useToast.getState().push({
        kind: 'success',
        title: 'Đã lưu',
        body: 'SKU YN-001 cập nhật +10',
        durationMs: 0,
      });
    });
    expect(screen.getByText('SKU YN-001 cập nhật +10')).toBeInTheDocument();
  });

  it('shows the idempotency-key + trace-id on error toasts only', () => {
    render(<ToastViewport />);
    act(() => {
      useToast.getState().push({
        kind: 'error',
        title: 'Lỗi',
        idempotencyKey: '01HXYZ',
        traceId: 'trace-abc',
        durationMs: 0,
      });
    });
    const trace = screen.getByTestId('toast-trace');
    expect(trace).toHaveTextContent('01HXYZ');
    expect(trace).toHaveTextContent('trace-abc');
  });

  it('does NOT show the trace block when the toast carries no key/trace', () => {
    render(<ToastViewport />);
    act(() => {
      useToast.getState().push({ kind: 'error', title: 'No trace', durationMs: 0 });
    });
    expect(screen.queryByTestId('toast-trace')).not.toBeInTheDocument();
  });

  it('does NOT show the trace block for success toasts even if a key is passed', () => {
    render(<ToastViewport />);
    act(() => {
      useToast.getState().push({
        kind: 'success',
        title: 'OK',
        idempotencyKey: '01HXYZ',
        durationMs: 0,
      });
    });
    expect(screen.queryByTestId('toast-trace')).not.toBeInTheDocument();
  });

  it('the close button dismisses the toast', async () => {
    const user = userEvent.setup();
    render(<ToastViewport />);
    act(() => {
      useToast.getState().push({ kind: 'success', title: 'OK', durationMs: 0 });
    });
    await user.click(screen.getByRole('button', { name: /Đóng thông báo/ }));
    expect(useToast.getState().toasts).toHaveLength(0);
    expect(screen.queryByTestId('toast-success')).not.toBeInTheDocument();
  });

  it('stacks multiple toasts and renders them all', () => {
    render(<ToastViewport />);
    act(() => {
      useToast.getState().push({ kind: 'success', title: 'Một', durationMs: 0 });
      useToast.getState().push({ kind: 'error', title: 'Hai', durationMs: 0 });
    });
    expect(screen.getByText('Một')).toBeInTheDocument();
    expect(screen.getByText('Hai')).toBeInTheDocument();
  });

  it('localises the dismiss button label to English when locale=en', () => {
    render(<ToastViewport />);
    act(() => {
      setLang('en');
      useToast.getState().push({ kind: 'success', title: 'Saved', durationMs: 0 });
    });
    expect(
      screen.getByRole('button', { name: /Dismiss notification/ }),
    ).toBeInTheDocument();
  });
});
