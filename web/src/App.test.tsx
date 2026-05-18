import { render, screen } from '@testing-library/react';
import { describe, it, expect } from 'vitest';
import App from './App';

describe('App (Sprint-6 U1 placeholder)', () => {
  it('renders the product name', () => {
    render(<App />);
    expect(screen.getByRole('heading', { name: /ShopFlow WMS/i })).toBeInTheDocument();
  });
});
