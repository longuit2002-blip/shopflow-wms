import { createFileRoute, redirect, useNavigate } from '@tanstack/react-router';
import { MfaEnrollScreen } from '../../components/auth/MfaEnrollScreen';
import { useAuth } from '../../hooks/useAuth';
import { useToast } from '../../hooks/useToast';

/**
 * Sprint-9.5 U6 — /mfa/enroll route. Gated by useAuth.authState ===
 * 'mfa-enrollment' via beforeLoad. Outside the `_auth` layout (the
 * user has no real session yet — only an intent token).
 */
export const Route = createFileRoute('/mfa/enroll')({
  beforeLoad: () => {
    const { authState } = useAuth.getState();
    if (authState !== 'mfa-enrollment') {
      throw redirect({ to: '/login' });
    }
  },
  component: MfaEnrollRoute,
});

function MfaEnrollRoute() {
  const navigate = useNavigate();
  const push = useToast((s) => s.push);
  return (
    <MfaEnrollScreen
      onEnrollmentComplete={() => navigate({ to: '/inventory' })}
      onSessionExpired={() => {
        push({
          kind: 'error',
          title: 'Enrollment session expired',
          body: 'Please log in again to retry MFA enrollment.',
        });
        navigate({ to: '/login' });
      }}
    />
  );
}
