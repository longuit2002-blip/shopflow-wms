import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { Sidebar } from './Sidebar';
import { __resetLocaleForTests, setLang } from '../../hooks/useLocale';

describe('Sidebar', () => {
  beforeEach(() => {
    __resetLocaleForTests();
  });

  afterEach(() => {
    __resetLocaleForTests();
  });

  it('renders 10 nav items including Inventory', () => {
    render(<Sidebar active="inventory" />);
    expect(screen.getByRole('button', { name: /Tồn kho/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Tổng quan/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Nhập hàng/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Đơn hàng/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Kênh bán/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Đồng bộ tồn/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Cài đặt/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Audit log/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Tenants/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Khởi tạo mới/i })).toBeInTheDocument();
  });

  it('marks active item with aria-current=page', () => {
    render(<Sidebar active="inventory" />);
    const inventory = screen.getByRole('button', { name: /Tồn kho/i });
    expect(inventory).toHaveAttribute('aria-current', 'page');

    const dashboard = screen.getByRole('button', { name: /Tổng quan/i });
    expect(dashboard).not.toHaveAttribute('aria-current');
  });

  it('hides the upcoming pill on the active item but shows it on stubs', () => {
    render(<Sidebar active="inventory" />);
    // Inventory is active — no Sprint-X pill
    const inventory = screen.getByRole('button', { name: /Tồn kho/i });
    expect(inventory.textContent).not.toMatch(/Sprint|Phase/);

    // Dashboard is stubbed — shows "Sprint 7"
    const dashboard = screen.getByRole('button', { name: /Tổng quan/i });
    expect(dashboard.textContent).toMatch(/Sprint 7/);
  });

  it('renders English labels after locale flip', () => {
    setLang('en');
    render(<Sidebar active="inventory" />);
    expect(screen.getByRole('button', { name: /Inventory/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Dashboard/i })).toBeInTheDocument();
  });

  it('renders Admin section header above the Settings group', () => {
    render(<Sidebar active="inventory" />);
    expect(screen.getByText(/Quản trị/i)).toBeInTheDocument();
  });

  it('fires onNavigate with the clicked id', async () => {
    const user = userEvent.setup();
    const onNavigate = vi.fn();
    render(<Sidebar active="inventory" onNavigate={onNavigate} />);

    await user.click(screen.getByRole('button', { name: /Tổng quan/i }));
    expect(onNavigate).toHaveBeenCalledWith('dashboard');
  });

  it('renders the dot-matrix logo + ShopFlow wordmark + version line', () => {
    render(<Sidebar active="inventory" />);
    expect(screen.getByRole('img', { name: /ShopFlow logo/i })).toBeInTheDocument();
    expect(screen.getByText('ShopFlow')).toBeInTheDocument();
    expect(screen.getByText(/WMS · v0\.9\.0/)).toBeInTheDocument();
  });
});
