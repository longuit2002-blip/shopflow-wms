/**
 * SeedTestOrderButton — Sprint-7 plan U10.
 *
 * Dev-only convenience: clicks `POST /api/outbound/orders/seed` so a fresh
 * order lands on the list without waiting for a real channel webhook. The
 * server endpoint itself returns 404 outside Development (gated by
 * ASPNETCORE_ENVIRONMENT=Development), so this button is hidden in any
 * non-dev Vite build to avoid a confusing failure.
 *
 * Behaviour:
 *   - Returns `null` when `import.meta.env.DEV` is falsy.
 *   - Click → `useSeedOrderMutation().mutateAsync({})` with the server's
 *     record-defaults shape (LineCount=3, ChannelPrefix=null).
 *   - Disabled while pending — anti-double-click matches Sprint-6 KTD12.
 *   - Success + error toasts are pushed by the mutation hook itself; this
 *     component does not push toasts directly.
 *
 * Lives in the Orders list-route header (right-side, beside the locale
 * switcher area). Plan moved ownership here from U13 because the button
 * renders on the list route, not the detail route.
 */

import { Plus } from 'lucide-react';
import { Button } from '../primitives/Button';
import { useSeedOrderMutation } from '../../hooks/useOrderMutations';
import { t, useLocale } from '../../hooks/useLocale';

const isDev = !!import.meta.env.DEV;

export function SeedTestOrderButton() {
  useLocale();
  const seedOrder = useSeedOrderMutation();

  if (!isDev) return null;

  return (
    <Button
      variant="primary"
      onClick={() => {
        void seedOrder.mutateAsync({});
      }}
      disabled={seedOrder.isPending}
      data-testid="seed-test-order"
      aria-busy={seedOrder.isPending || undefined}
    >
      <Plus size={13} aria-hidden />
      {t('Tạo đơn mẫu', 'Seed test order')}
    </Button>
  );
}
