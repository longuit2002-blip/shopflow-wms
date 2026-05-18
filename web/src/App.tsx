/**
 * App shell — Sprint-6 U3.
 *
 * Renders the desktop chrome (Sidebar + TopBar) around a content slot.
 * Until U6 wires TanStack Router, the slot shows a ComingSoon placeholder
 * for every screen except Inventory, and Inventory itself is a placeholder
 * until U9 ships the SKU table. Active route is hardcoded to `inventory`.
 *
 * Tenant + user identity are hardcoded to the design canon's demo fixture
 * (Yến Sào Khánh Hòa). Real auth lands in U4 + U5; this is what the user
 * sees post-login until then.
 */

import { Sidebar, type ScreenId } from './components/shell/Sidebar';
import { TopBar } from './components/shell/TopBar';
import { ComingSoon } from './components/primitives/ComingSoon';
import { Boxes } from 'lucide-react';
import { t, useLocale } from './hooks/useLocale';
import { useState } from 'react';

const DEMO_TENANT = {
  monogram: 'YK',
  legalName: 'Yến Sào Khánh Hòa Co., Ltd.',
  erc: '0408123456',
  region: 'Khánh Hòa',
  dbName: 'shopflow_yensaokhanhhoa',
};

const DEMO_USER = {
  name: 'Nguyễn Văn A',
  initials: 'NA',
};

export default function App() {
  // Subscribe so locale flips re-render the slot contents.
  useLocale();
  const [active, setActive] = useState<ScreenId>('inventory');

  return (
    <div style={{ display: 'flex', height: '100vh', minHeight: 0 }}>
      <Sidebar active={active} onNavigate={setActive} />
      <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minWidth: 0, minHeight: 0 }}>
        <TopBar tenant={DEMO_TENANT} user={DEMO_USER} />
        <main style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0, background: 'var(--bg)' }}>
          {active === 'inventory' ? (
            <InventoryPlaceholder />
          ) : (
            <ComingSoon
              icon={Boxes}
              screen={active}
              targetLabel={t('Sắp ra mắt', 'Coming soon')}
              blurb={t(
                'Các màn hình khác sẽ được mở khoá trong các sprint tới. Sprint-6 chỉ ship màn Tồn kho.',
                'Other screens unlock in later sprints. Sprint-6 only ships the Inventory screen.',
              )}
            />
          )}
        </main>
      </div>
    </div>
  );
}

function InventoryPlaceholder() {
  return (
    <div style={{ padding: 'var(--s-6)', flex: 1 }}>
      <h1 className="t-xl" style={{ margin: 0, fontWeight: 600 }}>
        {t('Tồn kho', 'Inventory')}
      </h1>
      <p className="t-sm" style={{ color: 'var(--ink-2)', marginTop: 'var(--s-2)' }}>
        {t(
          'SKU table, filter strip, KPI strip và reservation ledger drawer sẽ được wire trong U9 + U10.',
          'SKU table, filter strip, KPI strip, and reservation ledger drawer land in U9 + U10.',
        )}
      </p>
    </div>
  );
}
