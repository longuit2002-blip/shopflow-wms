import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import App from './App';

describe('App (Sprint-6 U2 placeholder)', () => {
  it('renders the product name heading', () => {
    render(<App />);
    expect(screen.getByRole('heading', { name: /ShopFlow WMS/i })).toBeInTheDocument();
  });

  it('renders the U2 token smoke pills', () => {
    render(<App />);
    expect(screen.getByText(/U2 token smoke/i)).toBeInTheDocument();
    expect(screen.getByText('neutral')).toBeInTheDocument();
    expect(screen.getByText('accent')).toBeInTheDocument();
  });
});
