import { describe, it, expect, vi, afterEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { Drawer } from './Drawer';
import { __resetLocaleForTests, setLang } from '../../hooks/useLocale';

afterEach(() => {
  __resetLocaleForTests();
});

describe('Drawer', () => {
  it('renders nothing when isOpen=false', () => {
    const { container } = render(
      <Drawer isOpen={false} onClose={() => {}} title="Hidden">
        <p>Body</p>
      </Drawer>,
    );
    expect(container).toBeEmptyDOMElement();
  });

  it('renders a dialog with aria-modal and aria-labelledby pointing at the title', () => {
    render(
      <Drawer isOpen onClose={() => {}} title="Sổ giữ chỗ">
        <p>Body</p>
      </Drawer>,
    );
    const dialog = screen.getByRole('dialog');
    expect(dialog).toHaveAttribute('aria-modal', 'true');
    const titleEl = screen.getByText('Sổ giữ chỗ');
    expect(dialog).toHaveAttribute('aria-labelledby', titleEl.id);
  });

  it('Esc closes the drawer', async () => {
    const onClose = vi.fn();
    const user = userEvent.setup();
    render(
      <Drawer isOpen onClose={onClose} title="Test">
        <p>Body</p>
      </Drawer>,
    );
    await user.keyboard('{Escape}');
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('clicking the backdrop closes the drawer', async () => {
    const onClose = vi.fn();
    const user = userEvent.setup();
    render(
      <Drawer isOpen onClose={onClose} title="Test">
        <p>Body</p>
      </Drawer>,
    );
    await user.click(screen.getByTestId('drawer-mask'));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('clicking the close button closes the drawer', async () => {
    const onClose = vi.fn();
    const user = userEvent.setup();
    render(
      <Drawer isOpen onClose={onClose} title="Test">
        <p>Body</p>
      </Drawer>,
    );
    await user.click(screen.getByRole('button', { name: /Đóng|Close/ }));
    expect(onClose).toHaveBeenCalledTimes(1);
  });

  it('moves focus to the close button when opened', () => {
    render(
      <Drawer isOpen onClose={() => {}} title="Test">
        <button type="button">Inner</button>
      </Drawer>,
    );
    const closeBtn = screen.getByRole('button', { name: /Đóng|Close/ });
    expect(document.activeElement).toBe(closeBtn);
  });

  it('honors the default width of 620 px', () => {
    render(
      <Drawer isOpen onClose={() => {}} title="Test">
        <p>Body</p>
      </Drawer>,
    );
    expect(screen.getByRole('dialog')).toHaveStyle({ width: '620px' });
  });

  it('honors a custom width prop', () => {
    render(
      <Drawer isOpen onClose={() => {}} title="Test" width={480}>
        <p>Body</p>
      </Drawer>,
    );
    expect(screen.getByRole('dialog')).toHaveStyle({ width: '480px' });
  });

  it('renders the headerExtra slot when provided', () => {
    render(
      <Drawer
        isOpen
        onClose={() => {}}
        title="Test"
        headerExtra={<span data-testid="extra">EXTRA</span>}
      >
        <p>Body</p>
      </Drawer>,
    );
    expect(screen.getByTestId('extra')).toBeInTheDocument();
  });

  it('inline-style animation references the slide-in keyframe (150 ms motion)', () => {
    render(
      <Drawer isOpen onClose={() => {}} title="Test">
        <p>Body</p>
      </Drawer>,
    );
    const dialog = screen.getByRole('dialog');
    expect(dialog.getAttribute('style') ?? '').toMatch(/drawerSlideIn/);
  });

  it('localises the close button label to English when locale=en', () => {
    setLang('en');
    render(
      <Drawer isOpen onClose={() => {}} title="Test">
        <p>Body</p>
      </Drawer>,
    );
    expect(screen.getByRole('button', { name: /Close/ })).toBeInTheDocument();
  });
});
