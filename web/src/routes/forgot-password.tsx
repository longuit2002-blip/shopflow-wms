import { createFileRoute, redirect, useNavigate } from '@tanstack/react-router';
import { ForgotPasswordScreen } from '../components/auth/ForgotPasswordScreen';

/**
 * Sprint-9.5 U6 — anonymous /forgot-password route. Same bypass-if-auth
 * shape as /login: authenticated users bounce to /inventory so the
 * back button doesn't strand them on the reset surface.
 */
export const Route = createFileRoute('/forgot-password')({
  beforeLoad: ({ context }) => {
    if (context.auth.isAuthenticated) {
      throw redirect({ to: '/inventory' });
    }
  },
  component: ForgotPasswordRoute,
});

function ForgotPasswordRoute() {
  const navigate = useNavigate();
  return <ForgotPasswordScreen onBackToLogin={() => navigate({ to: '/login' })} />;
}
