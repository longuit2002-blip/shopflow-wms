import { createFileRoute, redirect, useNavigate } from '@tanstack/react-router';
import { MfaChallengeScreen } from '../../components/auth/MfaChallengeScreen';
import { useAuth } from '../../hooks/useAuth';
import { useToast } from '../../hooks/useToast';

/**
 * Sprint-9.5 U6 — /mfa/challenge route. Gated by useAuth.authState ===
 * 'mfa-challenge' via beforeLoad — anyone else bounces to /login. The
 * `_auth` layout guard would also reject (no isAuthenticated yet) so we
 * intentionally place this OUTSIDE that layout.
 */
export const Route = createFileRoute('/mfa/challenge')({
  beforeLoad: () => {
    const { authState } = useAuth.getState();
    if (authState !== 'mfa-challenge') {
      throw redirect({ to: '/login' });
    }
  },
  component: MfaChallengeRoute,
});

function MfaChallengeRoute() {
  const navigate = useNavigate();
  const push = useToast((s) => s.push);
  return (
    <MfaChallengeScreen
      onSuccess={() => navigate({ to: '/inventory' })}
      onSessionExpired={() => {
        push({
          kind: 'error',
          title: 'Session expired',
          body: 'Please log in again.',
        });
        navigate({ to: '/login' });
      }}
    />
  );
}
