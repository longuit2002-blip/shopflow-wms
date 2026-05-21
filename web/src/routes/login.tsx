import { createFileRoute, redirect, useNavigate } from '@tanstack/react-router';
import { LoginScreen } from '../components/auth/LoginScreen';

/**
 * Login route — Sprint-6 plan U6.
 *
 * Bypasses the `_auth` guard so a logged-out user can reach it. If the
 * user IS authenticated, we bounce them to `/inventory` so the back
 * button + bookmarks don't strand them on the login screen.
 *
 * The screen calls `useAuth.login(jwt)` via LoginScreen; our
 * `onLoginSuccess` callback navigates to `/inventory`.
 */
export const Route = createFileRoute('/login')({
  beforeLoad: ({ context }) => {
    if (context.auth.isAuthenticated) {
      throw redirect({ to: '/inventory' });
    }
  },
  component: LoginRouteComponent,
});

function LoginRouteComponent() {
  const navigate = useNavigate();
  return (
    <LoginScreen
      onLoginSuccess={() => navigate({ to: '/inventory' })}
      onMfaChallenge={() => navigate({ to: '/mfa/challenge' })}
      onMfaEnrollment={() => navigate({ to: '/mfa/enroll' })}
      onForgotPassword={() => navigate({ to: '/forgot-password' })}
    />
  );
}
