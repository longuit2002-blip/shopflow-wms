import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Modal } from './Modal';
import { __resetLocaleForTests, setLang } from '../../hooks/useLocale';

afterEach(() => {
  __resetLocaleForTests();
});

describe('Modal', () => {
  it('renders nothing when isOpen=false', () => {
    const { container } = render(
      <Modal isOpen={false} onClose={() => {}} title="Hidden">
        <p>Body</p>
      </Modal>,
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('renders a dialog with aria-modal and aria-labelledby pointing at the title', () => {
    render(
      <Modal isOpen onClose={() => {}} title="Điều chỉnh tồn">
        <p>Body</p>
      </Modal>,
    );
    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveAttribute('aria-modal', 'true');
    const titleEl = screen.getByText('Điều chỉnh tồn');
    expect(dialog).toHaveAttribute('aria-labelledby', titleEl.id);
  });

  it('Esc closes the modal', async () => {
    const onClose = vi.fn();
    const user = userEvent.setup();
    render(
      <Modal isOpen onClose={onClose} title="Test">
        <p>Body</p>
      </Modal>,
    );
    await user.keyboard('{Escape}');
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('clicking the backdrop closes the modal by default', async () => {
    const onClose = vi.fn();
    const user = userEvent.setup();
    render(
      <Modal isOpen onClose={onClose} title="Test">
        <p>Body</p>
      </Modal>,
    );
    await user.click(screen.getByTestId('modal-mask'));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('respects dismissOnBackdrop=false (click on backdrop does NOT close)', async () => {
    const onClose = vi.fn();
    const user = userEvent.setup();
    render(
      <Modal isOpen onClose={onClose} title="Test" dismissOnBackdrop={false}>
        <p>Body</p>
      </Modal>,
    );
    await user.click(screen.getByTestId('modal-mask'));
    expect(onClose).not.toHaveBeenCalled();
  });

  it('Esc still closes the modal when dismissOnBackdrop=false', async () => {
    const onClose = vi.fn();
    const user = userEvent.setup();
    render(
      <Modal isOpen onClose={onClose} title="Test" dismissOnBackdrop={false}>
        <p>Body</p>
      </Modal>,
    );
    await user.keyboard('{Escape}');
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('clicking the close button closes the modal', async () => {
    const onClose = vi.fn();
    const user = userEvent.setup();
    render(
      <Modal isOpen onClose={onClose} title="Test">
        <p>Body</p>
      </Modal>,
    );
    await user.click(screen.getByRole('button', { name: /Đóng|Close/ }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('moves focus to the close button when opened', () => {
    render(
      <Modal isOpen onClose={() => {}} title="Test">
        <button type="button">Inner</button>
      </Modal>,
    );
    const closeBtn = screen.getByRole('button', { name: /Đóng|Close/ });
    expect(document.activeElement).toBe(closeBtn);
  });

  it('honours the default width of 480 px', () => {
    render(
      <Modal isOpen onClose={() => {}} title="Test">
        <p>Body</p>
      </Modal>,
    );
    expect(screen.getByRole('dialog')).toHaveStyle({ width: '480px' });
  });

  it('honours a custom width prop (e.g. 640 px for Create-SKU)', () => {
    render(
      <Modal isOpen onClose={() => {}} title="Test" width={640}>
        <p>Body</p>
      </Modal>,
    );
    expect(screen.getByRole('dialog')).toHaveStyle({ width: '640px' });
  });

  it('renders the footer slot when provided', () => {
    render(
      <Modal
        isOpen
        onClose={() => {}}
        title="Test"
        footer={<button data-testid="submit-btn">Submit</button>}
      >
        <p>Body</p>
      </Modal>,
    );
    expect(screen.getByTestId('submit-btn')).toBeInTheDocument();
  });

  it('inline-style animation references modalPopIn (150 ms motion)', () => {
    render(
      <Modal isOpen onClose={() => {}} title="Test">
        <p>Body</p>
      </Modal>,
    );
    expect(screen.getByRole('dialog').getAttribute('style') ?? '').toMatch(/modalPopIn/);
  });

  it('localises the close button label to English when locale=en', () => {
    setLang('en');
    render(
      <Modal isOpen onClose={() => {}} title="Test">
        <p>Body</p>
      </Modal>,
    );
    expect(screen.getByRole('button', { name: /Close/ })).toBeInTheDocument();
  });
});
