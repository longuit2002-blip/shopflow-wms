/**
 * Sprint-6 U1 placeholder shell — token-aware as of U2.
 *
 * Real router + auth + Inventory screen land in U6 + U9. This component
 * exists so the Vite scaffold has a renderable App for `pnpm dev` + `pnpm
 * build` smoke testing. The token chips below also serve as a visual
 * smoke test for U2: IBM Plex loads, amber-ochre accent renders, tabular
 * numerals align, and 1 px borders are visible.
 */
export default function App() {
  return (
    <main style={{ padding: 'var(--s-6)', maxWidth: 1280, margin: '0 auto' }}>
      <header style={{ marginBottom: 'var(--s-6)' }}>
        <h1 className="t-2xl" style={{ margin: 0, fontWeight: 600 }}>
          ShopFlow WMS
        </h1>
        <p className="t-sm" style={{ color: 'var(--ink-2)', marginTop: 'var(--s-2)' }}>
          Sprint-6 scaffold · vertical slice landing in U6.
        </p>
      </header>

      <section className="card" style={{ padding: 'var(--s-4)' }}>
        <div className="lbl" style={{ marginBottom: 'var(--s-3)' }}>
          U2 token smoke
        </div>
        <div style={{ display: 'flex', gap: 'var(--s-2)', flexWrap: 'wrap' }}>
          <span className="pill">neutral</span>
          <span className="pill ok">ok</span>
          <span className="pill warn">warn</span>
          <span className="pill bad">bad</span>
          <span className="pill info">info</span>
          <span className="pill accent">accent</span>
        </div>
        <div className="mono t-sm" style={{ marginTop: 'var(--s-3)', color: 'var(--ink-3)' }}>
          28.410.000 ₫ &middot; SO-2026-05-0042 &middot; idem_01HKDX_42_p3
        </div>
      </section>
    </main>
  );
}
