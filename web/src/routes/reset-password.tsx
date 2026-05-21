import { createFileRoute, redirect, useNavigate } from '@tanstack/react-router';
import { ResetPasswordScreen } from '../components/auth/ResetPasswordScreen';
import { useToast } from '../hooks/useToast';

interface ResetPasswordSearch {
  token?: string;
}

/**
 * Sprint-9.5 U6 — anonymous /reset-password route. Reads the `?token`
 * query param via TanStack Router's `validateSearch` typed accessor.
 * Invalid / missing tokens are handled inside the screen (error panel
 * with "Continue" → /forgot-password).
 */
export const Route = createFileRoute('/reset-password')({
  validateSearch: (search: Record<string, unknown>): ResetPasswordSearch => ({
    token: typeof search.token === 'string' ? search.token : undefined,
  }),
  beforeLoad: ({ context }) => {
    if (context.auth.isAuthenticated) {
      throw redirect({ to: '/inventory' });
    }
  },
  component: ResetPasswordRoute,
});

function ResetPasswordRoute() {
  const navigate = useNavigate();
  const { token } = Route.useSearch();
  const push = useToast((s) => s.push);

  return (
    <ResetPasswordScreen
      token={token ?? null}
      onResetComplete={() => {
        push({
          kind: 'success',
          title: 'Password reset',
          body: 'Please log in with your new password.',
        });
        navigate({ to: '/login' });
      }}
      onRequestNewLink={() => navigate({ to: '/forgot-password' })}
    />
  );
}
