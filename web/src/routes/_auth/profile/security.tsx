import { createFileRoute, useNavigate } from '@tanstack/react-router';
import { ProfileSecurityScreen } from '../../../components/auth/ProfileSecurityScreen';

/**
 * Sprint-9.5 U6 — /_auth/profile/security route. Under the `_auth`
 * layout — the parent's beforeLoad guard handles the
 * `isAuthenticated` check. Reading the user's MFA-enrolled status is
 * passed via a stub prop in Sprint-9.5; Sprint-10+ migrates to a
 * dedicated `useMe()` cache against `/api/auth/me`.
 */
export const Route = createFileRoute('/_auth/profile/security')({
  component: ProfileSecurityRoute,
});

function ProfileSecurityRoute() {
  const navigate = useNavigate();
  // Stub default — Sprint-10+ replaces with useMe() that reads
  // /api/auth/me + caches via TanStack Query.
  const mfaEnrolled = false;
  return (
    <ProfileSecurityScreen
      mfaEnrolled={mfaEnrolled}
      onMfaResetRequest={() => navigate({ to: '/inventory' })}
    />
  );
}
