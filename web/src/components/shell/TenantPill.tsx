/**
 * TenantPill — the leftmost identity chip in the TopBar.
 *
 * Ports the design canon `app.jsx` tenant-pill button (~line 128) without
 * the switch-tenant dropdown (Sprint-6 owner has one tenant; multi-tenant
 * Owner switching lands in Sprint-7 alongside real auth).
 *
 * The two-letter monogram is the user-facing tenant brand mark; below it
 * sit the legal name + ERC code + region + DB identifier. The DB
 * identifier is rendered in mono per STYLING_SPECS §5 ("DB name in
 * `--font-mono`") so on-call engineers can copy/paste it into psql.
 */

export interface TenantPillProps {
  /** 1–3 char monogram (e.g. "YK" for "Yến Sào Khánh Hòa"). */
  monogram: string;
  /** Tenant legal name; localized by caller. */
  legalName: string;
  /** Vietnamese ERC (Enterprise Registration Certificate) number. */
  erc: string;
  /** Region (e.g. "Khánh Hòa"). Caller is responsible for localization. */
  region: string;
  /** Per-tenant Postgres DB identifier (e.g. "shopflow_yensaokhanhhoa"). */
  dbName: string;
}

export function TenantPill({ monogram, legalName, erc, region, dbName }: TenantPillProps) {
  return (
    <div
      data-tenant-pill
      title={`${legalName} · ${erc}`}
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 10,
        padding: '4px 10px 4px 6px',
        background: 'transparent',
        border: '1px solid transparent',
        borderRadius: 'var(--radius-md)',
        minWidth: 0,
        maxWidth: 320,
        flex: '0 1 320px',
      }}
    >
      <div
        className="fs0"
        style={{
          width: 26,
          height: 26,
          borderRadius: 3,
          background: 'var(--ink)',
          color: 'var(--ink-inv)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          fontFamily: 'var(--font-mono)',
          fontWeight: 700,
          fontSize: 11,
        }}
      >
        {monogram}
      </div>
      <div style={{ textAlign: 'left', minWidth: 0, flex: 1 }}>
        <div className="tr" style={{ fontSize: 12.5, fontWeight: 600, lineHeight: 1.2 }}>
          {legalName}
        </div>
        <div
          className="tr"
          style={{ fontSize: 10.5, color: 'var(--ink-3)', lineHeight: 1.2 }}
        >
          <span className="mono">{erc}</span>
          {' · '}
          {region}
          {' · '}
          <span className="mono">db:{dbName}</span>
        </div>
      </div>
    </div>
  );
}
