import { Fragment, useState } from 'react';
import { createFileRoute } from '@tanstack/react-router';
import {
  Settings,
  ChevronRight,
  ChevronLeft,
  Building2,
  Database,
  ShieldCheck,
  History,
  FileSearch,
  Download,
  Info,
  Trash2,
  ExternalLink,
  AlertTriangle,
  ShieldAlert,
  X,
} from 'lucide-react';
import { Pill } from '../../components/primitives/Pill';
import { t, useLocale } from '../../hooks/useLocale';

/**
 * Compliance — the auditor's screen (design-handoff wedge screen #1).
 *
 * Ported from the design handoff `screen-compliance.jsx`. Header strip →
 * data residency → sub-processors → retention → tenant lifecycle → PDPA
 * rights. Builds the credibility wedge: VN legal references (Decree
 * 13/2023/NĐ-CP article numbers), sub-processor regions + DPA dates,
 * archive lifecycle as a state machine, full delete-tenant flow with a
 * typed-name + MFA + cool-off gate.
 *
 * Data is mocked in the frontend (no compliance backend endpoints exist
 * yet — wire to real APIs later). `data-review="..."` anchors preserved
 * from the handoff (QA + guided-tour contract).
 */

// ── Mock tenant + data ─────────────────────────────────────────────────────

const TENANT = {
  legal: 'Yến Sào Khánh Hòa',
  erc: 'ERC 4201588736',
  region: 'TP. Hồ Chí Minh',
  db: 'shopflow_yensaokhanhhoa',
};

interface SubProcessor {
  abbr: string;
  name: string;
  tag: string;
  svc: { vi: string; en: string };
  cats: string[];
  region: string;
  dpa: string;
}

const SUBPROCESSORS: SubProcessor[] = [
  {
    abbr: 'AWS',
    name: 'AWS RDS',
    tag: 'Postgres 16',
    svc: {
      vi: 'CSDL ứng dụng — cô lập vật lý từng tenant',
      en: 'Application database — physically isolated per tenant',
    },
    cats: ['PII', 'Transactional'],
    region: '🇸🇬 ap-southeast-1',
    dpa: '12/03/2026',
  },
  {
    abbr: 'EC',
    name: 'AWS ElastiCache',
    tag: 'Redis 7',
    svc: {
      vi: 'Cache phiên · giới hạn tốc · khoá phân tán',
      en: 'Session cache · rate limiting · distributed locks',
    },
    cats: ['Session'],
    region: '🇸🇬 ap-southeast-1',
    dpa: '12/03/2026',
  },
  {
    abbr: 'MQ',
    name: 'AWS MQ',
    tag: 'RabbitMQ 3.13',
    svc: {
      vi: 'Hàng đợi bất đồng bộ · điều phối saga',
      en: 'Async messaging · saga orchestration',
    },
    cats: ['Transactional'],
    region: '🇸🇬 ap-southeast-1',
    dpa: '12/03/2026',
  },
  {
    abbr: 'S3',
    name: 'AWS S3',
    tag: 'object',
    svc: {
      vi: 'Lưu file · snapshot lưu trữ mã hoá',
      en: 'File storage · encrypted archive snapshots',
    },
    cats: ['PII', 'Backup'],
    region: '🇸🇬 ap-southeast-1',
    dpa: '12/03/2026',
  },
  {
    abbr: 'GRA',
    name: 'Grafana Cloud',
    tag: 'observability',
    svc: {
      vi: 'Quan sát · metrics · logs · traces',
      en: 'Observability · metrics · logs · traces',
    },
    cats: ['Logs', 'Telemetry'],
    region: '🇪🇺 eu-central',
    dpa: '22/02/2026',
  },
  {
    abbr: 'SEN',
    name: 'Sentry',
    tag: 'errors',
    svc: { vi: 'Theo dõi lỗi · stack trace', en: 'Error tracking · stack traces' },
    cats: ['Telemetry', 'partial PII (scrubbed)'],
    region: '🇺🇸 us-east',
    dpa: '14/01/2026',
  },
  {
    abbr: 'SND',
    name: 'SendGrid',
    tag: 'email',
    svc: {
      vi: 'Email giao dịch — cảnh báo SLA, lời mời',
      en: 'Transactional email — SLA alerts, invitations',
    },
    cats: ['PII (email only)'],
    region: '🇺🇸 us-east',
    dpa: '14/01/2026',
  },
  {
    abbr: 'CLF',
    name: 'Cloudflare',
    tag: 'edge',
    svc: { vi: 'CDN · chống DDoS · WAF', en: 'CDN · DDoS protection · WAF' },
    cats: ['Network metadata'],
    region: '🌐 Global edge',
    dpa: '28/04/2026',
  },
];

interface RetentionRow {
  vi: string;
  en: string;
  rule_vi: string;
  rule_en: string;
}

const RETENTION: RetentionRow[] = [
  {
    vi: 'Đơn hàng đang xử lý',
    en: 'Active orders',
    rule_vi: 'Không giới hạn trong quan hệ kinh doanh',
    rule_en: 'Indefinite during business relationship',
  },
  {
    vi: 'Đơn hàng đã hoàn tất',
    en: 'Completed orders',
    rule_vi: '7 năm (Luật Kế toán Việt Nam)',
    rule_en: '7 years (Vietnam Accounting Law)',
  },
  {
    vi: 'Audit log',
    en: 'Audit log',
    rule_vi: '7 năm (NĐ 13 + Luật Kế toán)',
    rule_en: '7 years (Decree 13 + Accounting Law)',
  },
  {
    vi: 'Webhook payload thô',
    en: 'Raw webhook payloads',
    rule_vi: '30 ngày sau xử lý thành công',
    rule_en: '30 days after successful processing',
  },
  {
    vi: 'Phiên đăng nhập (session)',
    en: 'Login sessions',
    rule_vi: '30 ngày hoặc đến khi đăng xuất',
    rule_en: '30 days or until sign-out',
  },
  {
    vi: 'Dữ liệu cá nhân khách hàng cuối',
    en: 'End-customer personal data',
    rule_vi: 'Mặt nạ hoá sau 180 ngày kể từ giao dịch cuối',
    rule_en: 'Masked after 180 days since last transaction',
  },
  { vi: 'Log truy cập / API', en: 'Access / API logs', rule_vi: '90 ngày', rule_en: '90 days' },
  {
    vi: 'Bản sao lưu cơ sở dữ liệu',
    en: 'Database backups',
    rule_vi: '7 ngày (snapshot quay vòng)',
    rule_en: '7 days (rolling snapshots)',
  },
];

// ── Route ──────────────────────────────────────────────────────────────────

export const Route = createFileRoute('/_auth/compliance')({
  component: ComplianceRouteComponent,
});

function ComplianceRouteComponent() {
  useLocale();
  const [deleteOpen, setDeleteOpen] = useState(false);

  return (
    <div className="scroll-y" style={{ flex: 1 }}>
      <SettingsBreadcrumb crumb={t('Compliance', 'Compliance')} />

      <ComplianceHeader />

      <CompSection
        title={t('Vùng dữ liệu · Data residency', 'Data residency')}
        sub={t(
          'Nơi dữ liệu được lưu trữ, sao lưu, và sự ràng buộc về luồng dữ liệu xuyên biên giới.',
          'Where your data is stored, backed up, and the rules constraining cross-border flow.',
        )}
      >
        <ResidencyMap />
      </CompSection>

      <CompSection
        title={t('Nhà cung cấp phụ · Sub-processors', 'Sub-processors')}
        sub={t(
          '8 nhà cung cấp · cập nhật danh sách lần cuối 28 ngày trước',
          '8 providers · list last updated 28 days ago',
        )}
        action={
          <button className="btn sm" type="button">
            <Download size={11} strokeWidth={1.5} aria-hidden />{' '}
            {t('Tải bảng đối chiếu', 'Download attestation')}
          </button>
        }
      >
        <SubProcessorTable />
      </CompSection>

      <CompSection
        title={t('Chính sách lưu giữ · Retention policy', 'Retention policy')}
        sub={t(
          'Mỗi danh mục dữ liệu có thời hạn riêng — tự động thực thi bởi tiến trình lưu trữ.',
          'Each data category has its own retention horizon — enforced automatically by the archive pipeline.',
        )}
      >
        <RetentionTable />
      </CompSection>

      <CompSection
        title={t('Vòng đời tenant · Tenant lifecycle', 'Tenant lifecycle')}
        sub={t(
          'Trạng thái → quy tắc chuyển → kích hoạt. DROP DATABASE chỉ chạy sau 365 ngày kể từ Archive.',
          'State → transition rule → trigger. DROP DATABASE only runs 365 days after Archive.',
        )}
      >
        <LifecycleMachine />
      </CompSection>

      <CompSection
        title={t('Quyền của chủ dữ liệu · Data subject rights', 'Data subject rights')}
        sub={t(
          'NĐ 13/2023/NĐ-CP — Điều 9 (quyền truy cập, sửa, xoá, từ chối). Áp dụng cấp tenant.',
          'Decree 13/2023/NĐ-CP — Article 9 (rights of access, rectification, erasure, objection). Applied at tenant level.',
        )}
      >
        <RightsPanels onOpenDelete={() => setDeleteOpen(true)} />
      </CompSection>

      <div style={{ height: 40 }} />

      {deleteOpen && <DeleteTenantModal onClose={() => setDeleteOpen(false)} />}
    </div>
  );
}

function SettingsBreadcrumb({ crumb, sub }: { crumb: string; sub?: string }) {
  return (
    <div
      style={{
        padding: '12px 24px 0',
        display: 'flex',
        alignItems: 'center',
        gap: 6,
        fontSize: 11.5,
        color: 'var(--ink-3)',
      }}
    >
      <Settings size={11} strokeWidth={1.5} aria-hidden />
      <span>{t('Cài đặt', 'Settings')}</span>
      <ChevronRight size={11} strokeWidth={1.5} style={{ color: 'var(--ink-4)' }} aria-hidden />
      <span style={{ color: 'var(--ink)', fontWeight: 500 }}>{crumb}</span>
      {sub && (
        <Fragment>
          <span style={{ color: 'var(--ink-4)' }}>·</span>
          <span>{sub}</span>
        </Fragment>
      )}
    </div>
  );
}

function ComplianceHeader() {
  return (
    <div
      data-review="compliance-header"
      style={{
        borderTop: '1px solid var(--line)',
        borderBottom: '1px solid var(--line)',
        background: 'var(--bg-soft)',
        margin: '14px 0 0',
        padding: '14px 24px',
        display: 'grid',
        gridTemplateColumns: '1.1fr 1.2fr 1.3fr 1fr 1fr',
        gap: 24,
        alignItems: 'center',
      }}
    >
      <CompCol icon={Building2} label={t('Pháp nhân', 'Tenant identity')}>
        <div style={{ fontSize: 12.5, fontWeight: 600 }}>{TENANT.legal}</div>
        <div className="mono" style={{ fontSize: 10.5, color: 'var(--ink-3)' }}>
          {TENANT.erc} · {TENANT.region} · sg-1
        </div>
      </CompCol>
      <CompCol icon={Database} label={t('Cô lập · Isolation', 'Isolation guarantee')}>
        <div style={{ fontSize: 12, color: 'var(--ink-2)' }}>
          {t('CSDL vật lý riêng', 'Dedicated physical DB')}
        </div>
        <div
          className="mono"
          style={{ fontSize: 11, color: 'var(--ink)', userSelect: 'all', marginTop: 2 }}
        >
          {TENANT.db}
        </div>
      </CompCol>
      <CompCol icon={ShieldCheck} label={t('Trạng thái tuân thủ', 'Compliance status')}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
          <span className="live-dot ok" />
          <span style={{ fontSize: 12, fontWeight: 500 }}>
            {t('Tuân thủ NĐ 13/2023/NĐ-CP', 'Compliant — Decree 13/2023/NĐ-CP')}
          </span>
        </div>
        <div style={{ fontSize: 10.5, color: 'var(--ink-3)', marginTop: 2 }}>
          {t('Cập nhật 14:23 ngày 08/05', 'Refreshed 14:23 on 08/05')}
        </div>
      </CompCol>
      <CompCol icon={History} label={t('Cập nhật nhà cung cấp', 'Sub-processor change')}>
        <div style={{ fontSize: 12 }}>{t('28 ngày trước', '28 days ago')}</div>
        <a
          href="#subprocs"
          style={{ fontSize: 10.5, color: 'var(--ink-3)', textDecoration: 'underline' }}
        >
          {t('xem danh sách →', 'view list →')}
        </a>
      </CompCol>
      <CompCol icon={FileSearch} label={t('Audit log', 'Audit log')}>
        <div style={{ fontSize: 12 }}>
          <span className="mono tnum" style={{ fontWeight: 600 }}>
            3.247
          </span>{' '}
          {t('sự kiện', 'events')}
        </div>
        <div style={{ fontSize: 10.5, color: 'var(--ink-3)' }}>{t('24 giờ qua', 'last 24h')}</div>
      </CompCol>
    </div>
  );
}

function CompCol({
  icon: IconCmp,
  label,
  children,
}: {
  icon: typeof Building2;
  label: string;
  children: React.ReactNode;
}) {
  return (
    <div style={{ display: 'flex', gap: 10, alignItems: 'flex-start', minWidth: 0 }}>
      <IconCmp
        size={14}
        strokeWidth={1.5}
        style={{ color: 'var(--ink-3)', marginTop: 2 }}
        aria-hidden
      />
      <div style={{ minWidth: 0, flex: 1 }}>
        <div className="lbl" style={{ marginBottom: 2 }}>
          {label}
        </div>
        {children}
      </div>
    </div>
  );
}

function CompSection({
  title,
  sub,
  action,
  children,
}: {
  title: string;
  sub?: string;
  action?: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <section style={{ padding: '24px 24px 0' }}>
      <div style={{ display: 'flex', alignItems: 'flex-end', gap: 12, marginBottom: 14 }}>
        <div style={{ flex: 1, minWidth: 0 }}>
          <div style={{ fontSize: 15, fontWeight: 600, letterSpacing: '-0.005em' }}>{title}</div>
          {sub && (
            <div style={{ fontSize: 12, color: 'var(--ink-3)', marginTop: 2, maxWidth: 720 }}>
              {sub}
            </div>
          )}
        </div>
        {action}
      </div>
      {children}
    </section>
  );
}

function ResidencyMap() {
  return (
    <div
      data-review="residency"
      style={{
        display: 'flex',
        gap: 32,
        alignItems: 'flex-start',
        border: '1px solid var(--line)',
        borderRadius: 'var(--radius-lg)',
        padding: 18,
        background: 'var(--panel)',
      }}
    >
      <SEAMap />
      <div
        style={{ flex: 1, display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 14, minWidth: 0 }}
      >
        <ResidencyRow
          label={t('Vùng chính', 'Primary region')}
          value="Singapore (ap-southeast-1)"
          sub={t('Cô lập vật lý mỗi tenant', 'Physically isolated per tenant')}
          active
        />
        <ResidencyRow
          label={t('Vùng nhân bản', 'Replica region')}
          value={t('Không (gói MVP đơn vùng)', 'None (single-region by tier)')}
          sub={t('Đa vùng có ở gói Mid-Market', 'Multi-region in Mid-Market')}
        />
        <ResidencyRow
          label={t('Vùng sao lưu', 'Backup region')}
          value="Singapore (ap-southeast-1)"
          sub={t('Snapshot mã hoá · giữ 7 ngày', 'Encrypted snapshots · 7-day retention')}
        />
        <ResidencyRow
          label={t('Egress dữ liệu', 'Data egress')}
          value={t('Hạn chế trong ap-southeast-1', 'Restricted to ap-southeast-1')}
          sub={t('Không sao chép xuyên vùng', 'No cross-region replication')}
        />
      </div>
    </div>
  );
}

function ResidencyRow({
  label,
  value,
  sub,
  active,
}: {
  label: string;
  value: string;
  sub: string;
  active?: boolean;
}) {
  return (
    <div
      style={{
        paddingLeft: active ? 10 : 0,
        borderLeft: active ? '2px solid var(--accent)' : 'none',
        minWidth: 0,
      }}
    >
      <div className="lbl">{label}</div>
      <div className="mono" style={{ fontSize: 12, fontWeight: active ? 600 : 500, marginTop: 2 }}>
        {value}
      </div>
      <div style={{ fontSize: 11, color: 'var(--ink-3)', marginTop: 2 }}>{sub}</div>
    </div>
  );
}

function SEAMap() {
  return (
    <svg
      viewBox="0 0 240 120"
      width="240"
      height="120"
      style={{
        flex: 'none',
        background: 'var(--bg-sunken)',
        border: '1px solid var(--line)',
        borderRadius: 4,
      }}
      aria-hidden
    >
      <g fill="none" stroke="var(--line-strong)" strokeWidth="0.8">
        <path
          d="M134 18 L138 28 L142 38 L146 46 L148 54 L150 62 L148 70 L146 76 L142 80 L140 76 L136 70 L132 60 L130 50 L128 40 L130 28 Z"
          fill="var(--panel)"
        />
        <path
          d="M96 30 L120 28 L128 32 L130 50 L120 56 L112 60 L104 64 L100 70 L96 76 L86 74 L80 70 L78 60 L82 50 L88 40 Z"
          fill="var(--panel)"
        />
        <path d="M70 22 L82 28 L82 50 L78 60 L72 60 L66 50 L60 40 L58 30 Z" fill="var(--panel)" />
        <path d="M100 78 L110 82 L114 88 L110 94 L102 92 L98 86 Z" fill="var(--panel)" />
        <circle cx="104" cy="98" r="2.5" fill="var(--panel)" />
        <path d="M70 92 L90 100 L98 108 L86 112 L70 108 L60 100 Z" fill="var(--panel)" />
        <path d="M140 76 L168 78 L176 86 L172 96 L158 100 L144 96 L138 86 Z" fill="var(--panel)" />
        <path d="M120 108 L160 110 L172 112 L160 116 L130 116 L118 112 Z" fill="var(--panel)" />
        <path d="M188 50 L196 58 L192 68 L186 64 Z" fill="var(--panel)" />
        <path d="M198 72 L204 80 L200 86 L194 80 Z" fill="var(--panel)" />
      </g>
      <line
        x1="142"
        y1="62"
        x2="104"
        y2="98"
        stroke="var(--accent)"
        strokeWidth="0.8"
        strokeDasharray="2 2"
        opacity="0.55"
      />
      <circle cx="142" cy="62" r="2" fill="var(--ink-3)" />
      <text x="146" y="60" fontSize="7" fill="var(--ink-3)" fontFamily="var(--font-mono)">
        HCMC
      </text>
      <circle cx="104" cy="98" r="6" fill="var(--accent)" opacity="0.18" />
      <circle cx="104" cy="98" r="3" fill="var(--accent)" />
      <text
        x="60"
        y="116"
        fontSize="7"
        fill="var(--accent-ink)"
        fontFamily="var(--font-mono)"
        fontWeight="600"
      >
        SG · ap-southeast-1 · primary
      </text>
    </svg>
  );
}

function SubProcessorTable() {
  return (
    <div
      id="subprocs"
      data-review="subprocessors"
      style={{
        border: '1px solid var(--line)',
        borderRadius: 'var(--radius-lg)',
        overflow: 'hidden',
        background: 'var(--panel)',
      }}
    >
      <table className="t-data">
        <thead>
          <tr>
            <th style={{ width: 220 }}>{t('Nhà cung cấp', 'Provider')}</th>
            <th>{t('Dịch vụ', 'Service')}</th>
            <th style={{ width: 200 }}>{t('Danh mục dữ liệu', 'Data categories')}</th>
            <th style={{ width: 160 }}>{t('Khu vực', 'Region')}</th>
            <th style={{ width: 110 }}>{t('Cập nhật DPA', 'Last DPA')}</th>
            <th style={{ width: 40 }}>DPA</th>
          </tr>
        </thead>
        <tbody>
          {SUBPROCESSORS.map((p) => (
            <tr key={p.abbr}>
              <td>
                <div style={{ display: 'flex', alignItems: 'center', gap: 10 }}>
                  <div
                    className="fs0"
                    style={{
                      width: 30,
                      height: 30,
                      borderRadius: 3,
                      background: 'var(--bg-sunken)',
                      border: '1px solid var(--line)',
                      display: 'flex',
                      alignItems: 'center',
                      justifyContent: 'center',
                      fontFamily: 'var(--font-mono)',
                      fontSize: 10,
                      fontWeight: 700,
                      letterSpacing: '0.04em',
                    }}
                  >
                    {p.abbr}
                  </div>
                  <div style={{ minWidth: 0 }}>
                    <div style={{ fontSize: 12.5, fontWeight: 500 }}>{p.name}</div>
                    <div className="mono" style={{ fontSize: 10, color: 'var(--ink-3)' }}>
                      {p.tag}
                    </div>
                  </div>
                </div>
              </td>
              <td style={{ color: 'var(--ink-2)', fontSize: 12 }}>{t(p.svc.vi, p.svc.en)}</td>
              <td>
                <div style={{ display: 'flex', flexWrap: 'wrap', gap: 4 }}>
                  {p.cats.map((c) => (
                    <span key={c} className="cat-chip">
                      {c}
                    </span>
                  ))}
                </div>
              </td>
              <td style={{ fontSize: 12 }}>{p.region}</td>
              <td className="mono" style={{ fontSize: 11.5, color: 'var(--ink-2)' }}>
                {p.dpa}
              </td>
              <td>
                <a
                  href="#"
                  onClick={(e) => e.preventDefault()}
                  title="Open DPA"
                  aria-label={`Open DPA for ${p.name}`}
                >
                  <ExternalLink
                    size={12}
                    strokeWidth={1.5}
                    style={{ color: 'var(--ink-3)' }}
                    aria-hidden
                  />
                </a>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      <div
        style={{
          padding: '10px 14px',
          background: 'var(--bg-soft)',
          borderTop: '1px solid var(--line)',
          fontSize: 11.5,
          color: 'var(--ink-2)',
          lineHeight: 1.55,
        }}
      >
        {t(
          'Mọi nhà cung cấp phụ đều có thoả thuận xử lý dữ liệu (DPA) phù hợp với NĐ 13/2023/NĐ-CP. Tenant có quyền yêu cầu rà soát danh sách bất cứ lúc nào.',
          'Every sub-processor has a Data Processing Agreement aligned with Decree 13/2023/NĐ-CP. Tenants may request a review at any time.',
        )}
      </div>
    </div>
  );
}

function RetentionTable() {
  return (
    <div
      style={{
        border: '1px solid var(--line)',
        borderRadius: 'var(--radius-lg)',
        overflow: 'hidden',
        background: 'var(--panel)',
      }}
    >
      <table className="t-data">
        <thead>
          <tr>
            <th style={{ width: '36%' }}>{t('Danh mục dữ liệu', 'Data category')}</th>
            <th>{t('Thời hạn lưu giữ', 'Retention')}</th>
            <th style={{ width: 130, textAlign: 'right' }}>{t('Căn cứ', 'Basis')}</th>
          </tr>
        </thead>
        <tbody>
          {RETENTION.map((r, i) => (
            <tr key={r.en}>
              <td style={{ fontWeight: 500 }}>{t(r.vi, r.en)}</td>
              <td style={{ fontSize: 12, color: 'var(--ink-2)' }}>{t(r.rule_vi, r.rule_en)}</td>
              <td
                className="mono"
                style={{ fontSize: 10.5, color: 'var(--ink-3)', textAlign: 'right' }}
              >
                {i < 3 ? 'NĐ 13 · §17' : i < 6 ? 'NĐ 13 · §10' : 'Internal'}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

interface LifecycleState {
  id: string;
  label: string;
  rule: string;
  trig: string;
  kind?: 'current' | 'terminal';
}

function LifecycleMachine() {
  const states: LifecycleState[] = [
    {
      id: 'active',
      label: t('Hoạt động', 'Active'),
      rule: t('Sản xuất · không giới hạn', 'Production · indefinite'),
      trig: t('Mặc định', 'Default'),
      kind: 'current',
    },
    {
      id: 'susp',
      label: t('Tạm dừng', 'Suspended'),
      rule: t('Thủ công · 30 ngày làm nguội', 'Manual · 30-day cool-off'),
      trig: t('Operator', 'Operator'),
    },
    {
      id: 'pending',
      label: t('Chờ lưu trữ', 'Archive-pending'),
      rule: t('Thủ công · báo trước 90 ngày', 'Manual · 90-day notice'),
      trig: t('Owner + Ops', 'Owner + Ops'),
    },
    {
      id: 'archived',
      label: t('Đã lưu trữ', 'Archived'),
      rule: t('Tự động · chỉ đọc', 'Automatic · read-only'),
      trig: t('Sau 90 ngày', 'After 90 days'),
    },
    {
      id: 'drop',
      label: 'DROP DATABASE',
      rule: t('Tự động · sau 365 ngày', 'Automatic · after 365 days'),
      trig: t('Hệ thống', 'System'),
      kind: 'terminal',
    },
  ];
  return (
    <div
      style={{
        border: '1px solid var(--line)',
        borderRadius: 'var(--radius-lg)',
        background: 'var(--panel)',
        padding: '24px 18px',
      }}
    >
      <div style={{ display: 'flex', alignItems: 'stretch', gap: 4 }}>
        {states.map((s, i) => (
          <Fragment key={s.id}>
            <LifecycleNode state={s} />
            {i < states.length - 1 && <LifecycleArrow />}
          </Fragment>
        ))}
      </div>
      <div
        style={{
          marginTop: 14,
          padding: '10px 12px',
          background: 'var(--info-soft)',
          border: '1px solid var(--info-line)',
          borderRadius: 3,
          fontSize: 11.5,
          color: 'var(--info-ink, var(--ink-2))',
          display: 'flex',
          gap: 8,
          alignItems: 'flex-start',
        }}
      >
        <Info size={13} strokeWidth={1.75} style={{ marginTop: 1, flex: 'none' }} aria-hidden />
        <span>
          {t(
            'Hành trình Active → DROP DATABASE tổng cộng tối thiểu 485 ngày. Owner có thể huỷ tại bất kỳ điểm dừng nào trước Archive.',
            'The Active → DROP DATABASE journey takes a minimum of 485 days. The Owner can abort at any stop before Archive.',
          )}
        </span>
      </div>
    </div>
  );
}

function LifecycleNode({ state }: { state: LifecycleState }) {
  const isCurrent = state.kind === 'current';
  const isTerm = state.kind === 'terminal';
  return (
    <div
      style={{
        flex: 1,
        padding: '12px 12px',
        border:
          '1px solid ' + (isCurrent ? 'var(--accent)' : isTerm ? 'var(--bad-line)' : 'var(--line)'),
        borderTop: isCurrent
          ? '3px solid var(--accent)'
          : isTerm
            ? '3px solid var(--bad-line)'
            : '1px solid var(--line)',
        background: isCurrent
          ? 'var(--accent-soft)'
          : isTerm
            ? 'var(--bad-soft)'
            : 'var(--bg-soft)',
        borderRadius: 3,
        minWidth: 0,
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: 6, marginBottom: 6 }}>
        <span
          style={{
            width: 6,
            height: 6,
            borderRadius: 3,
            background: isCurrent ? 'var(--accent)' : isTerm ? 'var(--bad)' : 'var(--ink-4)',
          }}
        />
        <span style={{ fontSize: 11.5, fontWeight: 600, letterSpacing: '-0.005em' }}>
          {state.label}
        </span>
        {isCurrent && <Pill kind="ok">{t('hiện tại', 'current')}</Pill>}
      </div>
      <div style={{ fontSize: 11, color: 'var(--ink-2)', lineHeight: 1.4 }}>{state.rule}</div>
      <div
        className="mono"
        style={{
          fontSize: 10,
          color: 'var(--ink-3)',
          marginTop: 6,
          paddingTop: 6,
          borderTop: '1px dashed var(--line)',
        }}
      >
        → {state.trig}
      </div>
    </div>
  );
}

function LifecycleArrow() {
  return (
    <div
      style={{
        width: 18,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        flex: 'none',
      }}
    >
      <ChevronRight size={14} strokeWidth={1.5} style={{ color: 'var(--ink-4)' }} aria-hidden />
    </div>
  );
}

function RightsPanels({ onOpenDelete }: { onOpenDelete: () => void }) {
  return (
    <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 14 }}>
      <RightsPanel
        icon={Download}
        title={t('Xuất toàn bộ dữ liệu tenant', 'Export full tenant data')}
        desc={t(
          'Yêu cầu xuất toàn bộ dữ liệu của tenant này dưới định dạng SQL dump hoặc JSON. Email thông báo khi hoàn tất. Link tải hết hạn sau 7 ngày.',
          'Request the entire tenant dataset as SQL dump or JSON. Email notification on completion. Download link expires in 7 days.',
        )}
        meta={[
          { lbl: t('Kích thước ước tính', 'Estimated size'), val: '~ 47 MB' },
          { lbl: t('Thời gian', 'Estimated time'), val: t('8 phút', '8 minutes') },
          { lbl: t('Xuất lần cuối', 'Last export'), val: '15/04/2026 · 14:23' },
          { lbl: t('Bởi', 'Requested by'), val: 'Trần Minh Khôi' },
        ]}
        cta={
          <button className="btn primary" type="button">
            <Download size={12} strokeWidth={1.5} aria-hidden />{' '}
            {t('Yêu cầu xuất dữ liệu', 'Request data export')}
          </button>
        }
      />
      <RightsPanel
        icon={Trash2}
        kind="danger"
        title={t('Xoá tenant vĩnh viễn', 'Delete tenant permanently')}
        desc={t(
          'Khởi tạo tiến trình lưu trữ → DROP DATABASE sau 90 ngày. Yêu cầu xác minh tên tenant, MFA, và làm nguội 7 ngày. Tất cả thành viên sẽ nhận email cảnh báo.',
          'Initiates the archive pipeline → DROP DATABASE after 90 days. Requires typed name verification, MFA, and a 7-day cool-off. All members receive a warning email.',
        )}
        meta={[
          { lbl: t('Số bản ghi', 'Records'), val: '~ 124.000' },
          { lbl: t('Thành viên ảnh hưởng', 'Affected members'), val: '12' },
          { lbl: t('Có thể huỷ trong', 'Reversible within'), val: t('7 ngày', '7 days') },
          { lbl: t('Yêu cầu', 'Requires'), val: t('Owner · MFA', 'Owner · MFA') },
        ]}
        cta={
          <button
            className="btn"
            type="button"
            onClick={onOpenDelete}
            style={{
              borderColor: 'var(--bad-line)',
              color: 'var(--bad-ink)',
              background: 'transparent',
            }}
          >
            <Trash2 size={12} strokeWidth={1.5} aria-hidden />{' '}
            {t('Bắt đầu tiến trình xoá', 'Start deletion flow')}
          </button>
        }
      />
    </div>
  );
}

interface RightsMeta {
  lbl: string;
  val: string;
}

function RightsPanel({
  icon: IconCmp,
  title,
  desc,
  meta,
  cta,
  kind,
}: {
  icon: typeof Download;
  title: string;
  desc: string;
  meta: RightsMeta[];
  cta: React.ReactNode;
  kind?: 'danger';
}) {
  const danger = kind === 'danger';
  return (
    <div
      style={{
        border: '1px solid ' + (danger ? 'var(--bad-line)' : 'var(--line)'),
        borderRadius: 'var(--radius-lg)',
        padding: 20,
        background: 'var(--panel)',
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: 10, marginBottom: 8 }}>
        <div
          className="fs0"
          style={{
            width: 32,
            height: 32,
            borderRadius: 4,
            background: danger ? 'var(--bad-soft)' : 'var(--accent-soft)',
            color: danger ? 'var(--bad-ink)' : 'var(--accent-ink)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
          }}
        >
          <IconCmp size={15} strokeWidth={1.5} aria-hidden />
        </div>
        <div style={{ fontSize: 14, fontWeight: 600 }}>{title}</div>
      </div>
      <div style={{ fontSize: 12, color: 'var(--ink-2)', lineHeight: 1.55, marginBottom: 14 }}>
        {desc}
      </div>
      <div
        style={{
          display: 'grid',
          gridTemplateColumns: '1fr 1fr',
          gap: 8,
          marginBottom: 16,
          padding: 10,
          background: 'var(--bg-soft)',
          border: '1px solid var(--line)',
          borderRadius: 3,
        }}
      >
        {meta.map((m) => (
          <div key={m.lbl}>
            <div className="lbl">{m.lbl}</div>
            <div className="mono" style={{ fontSize: 11.5, marginTop: 1 }}>
              {m.val}
            </div>
          </div>
        ))}
      </div>
      {cta}
    </div>
  );
}

function DeleteTenantModal({ onClose }: { onClose: () => void }) {
  const [step, setStep] = useState(0);
  const [confirmName, setConfirmName] = useState('');
  const [mfa, setMfa] = useState('');
  const expected = TENANT.legal;
  const nameOk = confirmName === expected;
  const mfaOk = mfa.length === 6;
  const stepLabel = [
    t('xác minh tên', 'verify name'),
    t('xác thực MFA', 'verify MFA'),
    t('xác nhận cuối', 'final confirm'),
  ][step];

  return (
    <Fragment>
      <div
        onClick={onClose}
        style={{
          position: 'fixed',
          inset: 0,
          background: 'rgba(26, 26, 24, 0.40)',
          backdropFilter: 'blur(2px)',
          zIndex: 30,
        }}
      />
      <div
        role="dialog"
        aria-modal="true"
        aria-label={t('Xoá tenant vĩnh viễn', 'Permanently delete tenant')}
        style={{
          position: 'fixed',
          top: '50%',
          left: '50%',
          transform: 'translate(-50%, -50%)',
          width: 560,
          maxWidth: '92vw',
          background: 'var(--panel)',
          border: '1px solid var(--bad-line)',
          borderTop: '4px solid var(--bad)',
          borderRadius: 'var(--radius-lg)',
          boxShadow: 'var(--shadow-pop)',
          zIndex: 31,
        }}
      >
        <div
          style={{
            padding: '14px 20px',
            borderBottom: '1px solid var(--line)',
            display: 'flex',
            alignItems: 'center',
            gap: 10,
          }}
        >
          <ShieldAlert size={16} style={{ color: 'var(--bad-ink)' }} aria-hidden />
          <div style={{ flex: 1 }}>
            <div style={{ fontSize: 13.5, fontWeight: 600 }}>
              {t('Xoá tenant vĩnh viễn', 'Permanently delete tenant')}
            </div>
            <div style={{ fontSize: 11, color: 'var(--ink-3)' }}>
              {t('Bước', 'Step')} {step + 1} / 3 · {stepLabel}
            </div>
          </div>
          <button
            className="btn ghost sm"
            type="button"
            onClick={onClose}
            aria-label={t('Đóng', 'Close')}
          >
            <X size={14} aria-hidden />
          </button>
        </div>

        {step === 0 && (
          <div style={{ padding: 20 }}>
            <div
              style={{ fontSize: 12.5, color: 'var(--ink-2)', lineHeight: 1.6, marginBottom: 16 }}
            >
              {t(
                'Nhập chính xác tên pháp lý của tenant để xác nhận. Phân biệt hoa thường và dấu tiếng Việt.',
                'Type the tenant legal name to confirm. Case- and diacritic-sensitive.',
              )}
            </div>
            <div className="lbl">{t('Tên pháp lý cần nhập', 'Required legal name')}</div>
            <div
              className="mono"
              style={{
                fontSize: 12.5,
                padding: '8px 10px',
                background: 'var(--bg-sunken)',
                border: '1px solid var(--line)',
                borderRadius: 3,
                userSelect: 'all',
              }}
            >
              {expected}
            </div>
            <div style={{ marginTop: 14 }}>
              <div className="lbl">{t('Nhập xác nhận', 'Confirmation input')}</div>
              <input
                value={confirmName}
                onChange={(e) => setConfirmName(e.target.value)}
                placeholder={expected}
                style={{ width: '100%', fontFamily: 'var(--font-mono)' }}
              />
            </div>
            <div
              style={{
                marginTop: 18,
                padding: 12,
                background: 'var(--warn-soft)',
                border: '1px solid var(--warn-line)',
                borderRadius: 3,
                fontSize: 11.5,
                color: 'var(--warn-ink)',
                display: 'flex',
                gap: 8,
                alignItems: 'flex-start',
              }}
            >
              <AlertTriangle
                size={13}
                strokeWidth={1.75}
                style={{ marginTop: 1, flex: 'none' }}
                aria-hidden
              />
              <span>
                {t(
                  'Tiến trình này sẽ ghi vào audit log với mã idempotency. Mọi thành viên (12) sẽ nhận email trong 5 phút.',
                  'This action will be recorded in the audit log with an idempotency key. All 12 members receive an email within 5 minutes.',
                )}
              </span>
            </div>
          </div>
        )}

        {step === 1 && (
          <div style={{ padding: 20 }}>
            <div
              style={{ fontSize: 12.5, color: 'var(--ink-2)', lineHeight: 1.6, marginBottom: 16 }}
            >
              {t(
                'Nhập mã 6 chữ số từ ứng dụng xác thực của bạn (Authy / Google Authenticator).',
                'Enter the 6-digit code from your authenticator app (Authy / Google Authenticator).',
              )}
            </div>
            <div className="lbl">{t('Mã MFA', 'MFA code')}</div>
            <input
              value={mfa}
              onChange={(e) => setMfa(e.target.value.replace(/[^0-9]/g, '').slice(0, 6))}
              placeholder="••••••"
              maxLength={6}
              inputMode="numeric"
              style={{
                width: '100%',
                fontFamily: 'var(--font-mono)',
                fontSize: 18,
                letterSpacing: '0.4em',
                textAlign: 'center',
                padding: '12px 10px',
              }}
            />
            <div
              style={{ marginTop: 10, fontSize: 11, color: 'var(--ink-3)', textAlign: 'center' }}
            >
              {t('Mã làm mới sau 30 giây', 'Code refreshes every 30 seconds')}
            </div>
          </div>
        )}

        {step === 2 && (
          <div style={{ padding: 20 }}>
            <div
              style={{
                padding: 14,
                background: 'var(--bad-soft)',
                border: '1px solid var(--bad-line)',
                borderRadius: 3,
                marginBottom: 14,
              }}
            >
              <div
                style={{ fontSize: 12, color: 'var(--bad-ink)', fontWeight: 600, marginBottom: 6 }}
              >
                {t(
                  'Lịch trình lưu trữ — xem lại trước khi xác nhận',
                  'Archive schedule — review before confirming',
                )}
              </div>
              <div style={{ fontSize: 12, color: 'var(--ink-2)', lineHeight: 1.6 }}>
                {t(
                  'Tenant sẽ chuyển sang trạng thái Archive-pending lúc 23:59:59 ngày 19/05/2026. Bạn có 7 ngày để huỷ yêu cầu này.',
                  'Tenant transitions to Archive-pending at 23:59:59 on 19/05/2026. You have 7 days to cancel this request.',
                )}
              </div>
            </div>
            <div
              style={{
                display: 'grid',
                gridTemplateColumns: '120px 1fr',
                rowGap: 8,
                columnGap: 12,
                fontSize: 12,
              }}
            >
              <div className="lbl">{t('Trong 7 ngày', 'Within 7 days')}</div>
              <div>
                {t(
                  'Có thể huỷ. Email cảnh báo gửi 12 thành viên.',
                  'Reversible. Warning email sent to 12 members.',
                )}
              </div>
              <div className="lbl">{t('Sau 7 ngày', 'After 7 days')}</div>
              <div>
                {t(
                  'Chuyển Archive-pending. Tenant chỉ đọc.',
                  'Moves to Archive-pending. Tenant becomes read-only.',
                )}
              </div>
              <div className="lbl">{t('Sau 97 ngày', 'After 97 days')}</div>
              <div>
                {t('Chuyển Archived. Không truy cập qua app.', 'Moves to Archived. No app access.')}
              </div>
              <div className="lbl">{t('Sau 462 ngày', 'After 462 days')}</div>
              <div className="mono">DROP DATABASE {TENANT.db}</div>
            </div>
          </div>
        )}

        <div
          style={{
            padding: 14,
            borderTop: '1px solid var(--line)',
            background: 'var(--bg-soft)',
            display: 'flex',
            gap: 8,
          }}
        >
          <button className="btn" type="button" onClick={onClose}>
            {t('Huỷ', 'Cancel')}
          </button>
          <span style={{ flex: 1 }} />
          {step > 0 && (
            <button className="btn" type="button" onClick={() => setStep(step - 1)}>
              <ChevronLeft size={11} aria-hidden /> {t('Quay lại', 'Back')}
            </button>
          )}
          {step < 2 && (
            <button
              className="btn primary"
              type="button"
              disabled={(step === 0 && !nameOk) || (step === 1 && !mfaOk)}
              onClick={() => setStep(step + 1)}
            >
              {t('Tiếp theo', 'Next')} <ChevronRight size={11} aria-hidden />
            </button>
          )}
          {step === 2 && (
            <button
              className="btn"
              type="button"
              onClick={onClose}
              style={{
                background: 'var(--bad)',
                color: 'var(--ink-inv)',
                borderColor: 'var(--bad)',
              }}
            >
              <Trash2 size={12} strokeWidth={1.5} aria-hidden />{' '}
              {t('Xác nhận xoá tenant', 'Confirm tenant deletion')}
            </button>
          )}
        </div>
      </div>
    </Fragment>
  );
}
