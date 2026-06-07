import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { RolePermissionsEditor } from './RolePermissionsEditor';
import { __resetLocaleForTests } from '../../hooks/useLocale';
import { PERMISSION_KEYS } from '../../api/admin';

const INITIAL = {
  Owner: PERMISSION_KEYS.map((p) => p.key),
  Picker: ['inventory.read'],
  Dispatcher: ['outbound.orders.read'],
};

describe('RolePermissionsEditor (Sprint-9.5 U7 — F5 / AE5)', () => {
  beforeEach(() => __resetLocaleForTests());
  afterEach(() => __resetLocaleForTests());

  it('renders Owner / Picker / Dispatcher column headers + module groups', () => {
    render(<RolePermissionsEditor initial={INITIAL} canEdit={true} onSave={vi.fn()} />);
    expect(screen.getByRole('columnheader', { name: /Owner/i })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: /Picker/i })).toBeInTheDocument();
    expect(screen.getByRole('columnheader', { name: /Dispatcher/i })).toBeInTheDocument();
    // 5 module group rows.
    expect(screen.getByText(/^▾? ?Auth/)).toBeInTheDocument();
    expect(screen.getByText(/^▾? ?Inventory/)).toBeInTheDocument();
  });

  it('Owner column checkboxes are disabled (KTD11 visualization)', () => {
    render(<RolePermissionsEditor initial={INITIAL} canEdit={true} onSave={vi.fn()} />);
    const ownerInventoryRead = screen.getByLabelText(/Owner inventory\.read/i) as HTMLInputElement;
    expect(ownerInventoryRead.disabled).toBe(true);
    expect(ownerInventoryRead.checked).toBe(true);
    expect(ownerInventoryRead.getAttribute('aria-describedby')).toBeTruthy();
  });

  it('Picker checkbox toggle updates state', async () => {
    const user = userEvent.setup();
    render(<RolePermissionsEditor initial={INITIAL} canEdit={true} onSave={vi.fn()} />);
    const cb = screen.getByLabelText(/Picker inventory\.adjust/i) as HTMLInputElement;
    expect(cb.checked).toBe(false);
    await user.click(cb);
    expect(cb.checked).toBe(true);
  });

  it('Save button disabled when no diff', () => {
    render(<RolePermissionsEditor initial={INITIAL} canEdit={true} onSave={vi.fn()} />);
    expect(screen.getByRole('button', { name: /Save changes|Lưu thay đổi/i })).toBeDisabled();
  });

  it('Save flow: toggle Picker → confirm modal shows diff → Continue fires PUT once for Picker', async () => {
    const user = userEvent.setup();
    const onSave = vi.fn().mockResolvedValue(undefined);
    render(<RolePermissionsEditor initial={INITIAL} canEdit={true} onSave={onSave} />);

    // Toggle a Picker permission to create a diff.
    await user.click(screen.getByLabelText(/Picker inventory\.adjust/i));
    expect(screen.getByRole('button', { name: /Save changes|Lưu thay đổi/i })).toBeEnabled();

    await user.click(screen.getByRole('button', { name: /Save changes|Lưu thay đổi/i }));
    const dialog = await screen.findByRole('dialog');
    expect(dialog).toBeInTheDocument();
    expect(dialog.textContent).toContain('Picker');
    expect(dialog.textContent).toContain('inventory.adjust');

    await user.click(screen.getByRole('button', { name: /^Continue$|^Tiếp tục$/i }));

    await waitFor(() => expect(onSave).toHaveBeenCalledTimes(1));
    expect(onSave).toHaveBeenCalledWith(
      'Picker',
      expect.arrayContaining(['inventory.read', 'inventory.adjust']),
    );
  });

  it('canEdit=false disables all Picker + Dispatcher checkboxes', () => {
    render(<RolePermissionsEditor initial={INITIAL} canEdit={false} onSave={vi.fn()} />);
    const pickerBox = screen.getByLabelText(/Picker inventory\.read/i) as HTMLInputElement;
    expect(pickerBox.disabled).toBe(true);
    const dispatcherBox = screen.getByLabelText(/Dispatcher outbound\.orders\.read/i) as HTMLInputElement;
    expect(dispatcherBox.disabled).toBe(true);
  });

  it('toggling and untoggling the same key shows no Save diff', async () => {
    const user = userEvent.setup();
    render(<RolePermissionsEditor initial={INITIAL} canEdit={true} onSave={vi.fn()} />);

    const cb = screen.getByLabelText(/Picker inventory\.adjust/i);
    await user.click(cb);
    await user.click(cb);

    expect(screen.getByRole('button', { name: /Save changes|Lưu thay đổi/i })).toBeDisabled();
  });
});
