import { render, screen } from '@testing-library/react';
import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import { TopBar } from './TopBar';
import { __resetLocaleForTests, setLang } from '../../hooks/useLocale';

const tenant = {
  monogram: 'YK',
  legalName: 'Yến Sào Khánh Hòa Co., Ltd.',
  erc: '0408123456',
  region: 'Khánh Hòa',
  dbName: 'shopflow_yensaokhanhhoa',
};

const user = {
  name: 'Nguyễn Văn A',
  initials: 'NA',
};

describe('TopBar', () => {
  beforeEach(() => {
    __resetLocaleForTests();
  });

  afterEach(() => {
    __resetLocaleForTests();
  });

  it('renders as a banner landmark', () => {
    render(<TopBar tenant={tenant} user={user} />);
    expect(screen.getByRole('banner')).toBeInTheDocument();
  });

  it('renders the tenant pill', () => {
    render(<TopBar tenant={tenant} user={user} />);
    expect(screen.getByText(/Yến Sào Khánh Hòa Co\., Ltd\./)).toBeInTheDocument();
  });

  it('renders the user name and Owner role label (vi default)', () => {
    render(<TopBar tenant={tenant} user={user} />);
    expect(screen.getByText('Nguyễn Văn A')).toBeInTheDocument();
    expect(screen.getByText(/Chủ tài khoản/)).toBeInTheDocument();
  });

  it('renders Owner role label in English after locale flip', () => {
    setLang('en');
    render(<TopBar tenant={tenant} user={user} />);
    expect(screen.getByText('Owner')).toBeInTheDocument();
  });

  it('renders the help and notifications buttons with accessible labels', () => {
    render(<TopBar tenant={tenant} user={user} />);
    expect(screen.getByRole('button', { name: /Trợ giúp/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Thông báo/i })).toBeInTheDocument();
  });

  it('renders the locale switcher with VI pressed', () => {
    render(<TopBar tenant={tenant} user={user} />);
    expect(screen.getByRole('button', { name: /Tiếng Việt/i })).toHaveAttribute(
      'aria-pressed',
      'true',
    );
  });
});
