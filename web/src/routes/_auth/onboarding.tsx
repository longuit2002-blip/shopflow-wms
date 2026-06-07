import { Fragment, useEffect, useRef, useState } from 'react';
import { createFileRoute } from '@tanstack/react-router';
import {
  UserPlus,
  ChevronLeft,
  ChevronRight,
  Check,
  Info,
  Rocket,
  Activity,
  ArrowRight,
  Download,
  AlertTriangle,
  RotateCcw,
  Bug,
  Trash2,
} from 'lucide-react';
import { Pill } from '../../components/primitives/Pill';
import { t, useLocale } from '../../hooks/useLocale';

/**
 * Tenant onboarding — the operator-runs-it, customer-sees-it wizard
 * (design-handoff wedge screen #3).
 *
 * Ports the design handoff `screen-onboarding.jsx`. A 4-step wizard
 * (business identity → initial admin user → sub-processor / PDPA
 * disclosure → provision) hands off to a live provisioning view that
 * animates the catalog-row state machine (Postgres DB create → migrations
 * → seed → tenant routing → health check), then resolves to a success or
 * a partial-state failure-recovery surface.
 *
 * The credibility wedge: BEFORE signup the operator sees that this
 * tenant's database is created at `shopflow_<slug>` in ap-southeast-1
 * (Singapore) with physical isolation — the database-per-tenant guarantee
 * is surfaced at the auto-derived "Database identifier" field on step 1
 * and again in the provisioning summary + success record.
 *
 * Data is mocked in the frontend (no provisioning backend endpoints are
 * wired here; `shopflow-migrate provision` does the real work server-side).
 * Provisioning timing is a `setTimeout` choreography mirroring the handoff.
 * `data-review` / `data-tour` anchors preserved from the handoff.
 */

// ── Mock data ────────────────────────────────────────────────────────────────

type ProvStatus = 'pending' | 'active' | 'done' | 'fail';

type ProvKey = 'db' | 'mig' | 'seed' | 'route' | 'health';

type ProvProgress = Record<ProvKey, ProvStatus>;

interface OnboardForm {
  legal: string;
  regKind: string;
  regNum: string;
  region: string;
  tier: string;
  adminName: string;
  adminEmail: string;
  adminRole: string;
  sub1: boolean;
  sub2: boolean;
  sub3: boolean;
  sub4: boolean;
  pdpa: boolean;
}

const SUB_KEYS = ['sub1', 'sub2', 'sub3', 'sub4'] as const;

type SubKey = (typeof SUB_KEYS)[number];

interface SubProcessor {
  name: string;
  provider: string;
  purpose: { vi: string; en: string };
  region: string;
  updated: string;
}

const SUBPROCESSORS: SubProcessor[] = [
  {
    name: 'Postgres (per-tenant)',
    provider: 'Self-managed RDS · ap-southeast-1',
    purpose: { vi: 'Kho dữ liệu tenant · cô lập vật lý', en: 'Tenant data store' },
    region: 'SG-1',
    updated: '2026-03-04',
  },
  {
    name: 'Redis',
    provider: 'ElastiCache · ap-southeast-1',
    purpose: { vi: 'Cache đặt chỗ · khoá idempotency', en: 'Reservation cache · idempotency keys' },
    region: 'SG-1',
    updated: '2026-02-12',
  },
  {
    name: 'RabbitMQ',
    provider: 'AmazonMQ · ap-southeast-1',
    purpose: { vi: 'Nhận webhook · message saga', en: 'Webhook ingest · saga messages' },
    region: 'SG-1',
    updated: '2026-02-12',
  },
  {
    name: 'Observability (Tempo/Loki)',
    provider: 'Grafana Cloud · SG',
    purpose: { vi: 'Trace, log, metrics', en: 'Traces, logs, metrics' },
    region: 'SG-1',
    updated: '2026-01-30',
  },
];

interface ProvStep {
  key: ProvKey;
  label: { vi: string; en: string };
  sub: { vi: string; en: string };
  traceTail: string;
  latencyMs: number;
}

const PROVISIONING_STEPS: ProvStep[] = [
  {
    key: 'db',
    label: { vi: 'Tạo cơ sở dữ liệu Postgres', en: 'Postgres database created' },
    sub: { vi: 'shopflow_yensaokhanhhoa · SG-1', en: 'shopflow_yensaokhanhhoa · SG-1' },
    traceTail: '0x7af3',
    latencyMs: 1820,
  },
  {
    key: 'mig',
    label: { vi: 'Áp dụng migration', en: 'Migrations applied' },
    sub: { vi: '47 / 47 · checkpoint 0047', en: '47 / 47 · checkpoint 0047' },
    traceTail: '0x7af4',
    latencyMs: 9412,
  },
  {
    key: 'seed',
    label: { vi: 'Nạp dữ liệu khởi tạo', en: 'Seed data loaded' },
    sub: {
      vi: '4 kênh · zone mặc định A-D · 3 vai trò',
      en: '4 channels · default zones A-D · 3 roles',
    },
    traceTail: '0x7af5',
    latencyMs: 642,
  },
  {
    key: 'route',
    label: { vi: 'Đăng ký định tuyến tenant', en: 'Tenant routing registered' },
    sub: {
      vi: 'shopflow_yensaokhanhhoa → SG-1 cluster',
      en: 'shopflow_yensaokhanhhoa → SG-1 cluster',
    },
    traceTail: '0x7af6',
    latencyMs: 188,
  },
  {
    key: 'health',
    label: { vi: 'Kiểm tra sức khoẻ', en: 'Health-check pass' },
    sub: {
      vi: 'Reservation API · webhook ingest · SignalR hub',
      en: 'Reservation API · webhook ingest · SignalR hub',
    },
    traceTail: '0x7af7',
    latencyMs: 412,
  },
];

const MIGRATION_TOTAL = 47;

// Auto-derived per-tenant database identifier — the database-per-tenant
// guarantee surfaced before signup. Mirrors the handoff slug rule:
// lowercased, non-alphanumerics stripped, capped at 32 chars, `shopflow_`.
function dbNameFromLegal(legal: string): string {
  return `shopflow_${legal
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '')
    .slice(0, 32)}`;
}

// ── Route ──────────────────────────────────────────────────────────────────

export const Route = createFileRoute('/_auth/onboarding')({
  component: OnboardingRouteComponent,
});

type Phase = 'wizard' | 'provisioning' | 'success' | 'failure';

function OnboardingRouteComponent() {
  useLocale();
  const [step, setStep] = useState(1);
  const [phase, setPhase] = useState<Phase>('wizard');
  const [form, setForm] = useState<OnboardForm>({
    legal: 'Yến Sào Khánh Hòa Co., Ltd.',
    regKind: 'ERC',
    regNum: '4201234567',
    region: 'SG-1',
    tier: 'Mid-market',
    adminName: 'Trần Minh Khôi',
    adminEmail: 'khoi.tran@yensaokh.vn',
    adminRole: 'admin',
    sub1: false,
    sub2: false,
    sub3: false,
    sub4: false,
    pdpa: false,
  });

  // Provisioning state
  const [progress, setProgress] = useState<ProvProgress>({
    db: 'pending',
    mig: 'pending',
    seed: 'pending',
    route: 'pending',
    health: 'pending',
  });
  const [migCount, setMigCount] = useState(0);
  const [elapsed, setElapsed] = useState(0);
  const [demoFail, setDemoFail] = useState(false);

  useEffect(() => {
    if (phase !== 'provisioning') return;
    const start = Date.now();
    const tick = window.setInterval(() => setElapsed((Date.now() - start) / 1000), 100);

    const t1 = window.setTimeout(() => setProgress((p) => ({ ...p, db: 'active' })), 100);
    const t2 = window.setTimeout(
      () => setProgress((p) => ({ ...p, db: 'done', mig: 'active' })),
      1900,
    );
    const migInt = window.setInterval(() => {
      setMigCount((c) => Math.min(MIGRATION_TOTAL, c + 4));
    }, 360);
    const t3 = window.setTimeout(
      () => {
        window.clearInterval(migInt);
        setMigCount(MIGRATION_TOTAL);
        if (demoFail) {
          setProgress((p) => ({ ...p, mig: 'fail' }));
          setPhase('failure');
          window.clearInterval(tick);
        } else {
          setProgress((p) => ({ ...p, mig: 'done', seed: 'active' }));
        }
      },
      demoFail ? 5500 : 11300,
    );
    const t4 = window.setTimeout(
      () => setProgress((p) => ({ ...p, seed: 'done', route: 'active' })),
      11900,
    );
    const t5 = window.setTimeout(
      () => setProgress((p) => ({ ...p, route: 'done', health: 'active' })),
      12080,
    );
    const t6 = window.setTimeout(() => {
      setProgress((p) => ({ ...p, health: 'done' }));
      setPhase('success');
      window.clearInterval(tick);
    }, 12492);

    return () => {
      [t1, t2, t3, t4, t5, t6].forEach((id) => window.clearTimeout(id));
      window.clearInterval(migInt);
      window.clearInterval(tick);
    };
  }, [phase, demoFail]);

  const subsAllAcked = form.sub1 && form.sub2 && form.sub3 && form.sub4 && form.pdpa;

  return (
    <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0 }}>
      <div className="strip">
        <UserPlus size={14} strokeWidth={1.5} aria-hidden />
        <span className="t">{t('Khởi tạo tenant', 'Tenant onboarding')}</span>
        <span style={{ fontSize: 11.5, color: 'var(--ink-3)' }}>
          ·{' '}
          {t(
            'bảng điều khiển nội bộ · hiển thị cho khách sau khi đăng ký',
            'internal operator console · customer-visible after signup',
          )}
        </span>
        <span style={{ flex: 1 }} />
        {phase === 'wizard' && (
          <span className="mono tnum" style={{ fontSize: 11, color: 'var(--ink-3)' }}>
            {t('Bước', 'Step')} {step} / 4
          </span>
        )}
      </div>

      {phase === 'wizard' && (
        <div className="scroll-y" style={{ flex: 1, padding: '24px 0' }}>
          <div style={{ maxWidth: 880, margin: '0 auto', padding: '0 24px' }}>
            <Stepper step={step} />
            <div style={{ marginTop: 24 }}>
              {step === 1 && <WizardStep1 form={form} setForm={setForm} />}
              {step === 2 && <WizardStep2 form={form} setForm={setForm} />}
              {step === 3 && <WizardStep3 form={form} setForm={setForm} />}
              {step === 4 && (
                <WizardStep4 form={form} demoFail={demoFail} setDemoFail={setDemoFail} />
              )}
            </div>

            <div
              style={{
                marginTop: 28,
                display: 'flex',
                gap: 8,
                paddingTop: 18,
                borderTop: '1px solid var(--line)',
              }}
            >
              {step > 1 && (
                <button className="btn" type="button" onClick={() => setStep(step - 1)}>
                  <ChevronLeft size={12} aria-hidden /> {t('Quay lại', 'Back')}
                </button>
              )}
              <span style={{ flex: 1 }} />
              {step < 4 && (
                <button
                  className="btn primary"
                  type="button"
                  disabled={step === 3 && !subsAllAcked}
                  onClick={() => setStep(step + 1)}
                >
                  {t('Tiếp tục', 'Continue')} <ChevronRight size={12} aria-hidden />
                </button>
              )}
              {step === 4 && (
                <button
                  className="btn accent"
                  type="button"
                  onClick={() => setPhase('provisioning')}
                >
                  <Rocket size={12} aria-hidden /> {t('Khởi tạo tenant', 'Provision tenant')}
                </button>
              )}
            </div>
          </div>
        </div>
      )}

      {phase === 'provisioning' && (
        <ProvisioningView form={form} progress={progress} migCount={migCount} elapsed={elapsed} />
      )}
      {phase === 'success' && <SuccessView form={form} elapsed={elapsed} />}
      {phase === 'failure' && (
        <FailureView
          onRetry={() => {
            setDemoFail(false);
            setProgress({
              db: 'done',
              mig: 'pending',
              seed: 'pending',
              route: 'pending',
              health: 'pending',
            });
            setMigCount(0);
            setPhase('provisioning');
          }}
        />
      )}
    </div>
  );
}

// ── Stepper ──────────────────────────────────────────────────────────────────

function Stepper({ step }: { step: number }) {
  const steps = [
    t('Pháp nhân', 'Business identity'),
    t('Quản trị viên', 'Admin user'),
    t('Công bố nhà cung cấp phụ', 'Sub-processor disclosure'),
    t('Khởi tạo', 'Provision'),
  ];
  return (
    <div data-tour="onboarding-stepper" style={{ display: 'flex', alignItems: 'center', gap: 8 }}>
      {steps.map((label, i) => {
        const n = i + 1;
        const done = step > n;
        const current = step === n;
        return (
          <Fragment key={label}>
            <div
              style={{
                display: 'flex',
                alignItems: 'center',
                gap: 8,
                opacity: n <= step ? 1 : 0.45,
              }}
            >
              <div
                className="fs0"
                style={{
                  width: 26,
                  height: 26,
                  borderRadius: 13,
                  border: '1px solid var(--line-strong)',
                  background: done ? 'var(--ok)' : current ? 'var(--ink)' : 'var(--panel)',
                  color: step >= n ? 'var(--ink-inv)' : 'var(--ink-3)',
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  fontSize: 11,
                  fontWeight: 600,
                }}
              >
                {done ? <Check size={12} strokeWidth={3} aria-hidden /> : n}
              </div>
              <span style={{ fontSize: 12.5, fontWeight: current ? 600 : 500 }}>{label}</span>
            </div>
            {i < steps.length - 1 && (
              <div style={{ flex: 1, height: 1, background: 'var(--line)' }} />
            )}
          </Fragment>
        );
      })}
    </div>
  );
}

// ── Form primitives ────────────────────────────────────────────────────────

function FormField({
  label,
  sub,
  children,
}: {
  label: string;
  sub?: string;
  children: React.ReactNode;
}) {
  return (
    <div style={{ marginBottom: 16 }}>
      <div className="lbl" style={{ marginBottom: 4 }}>
        {label}
      </div>
      {sub && <div style={{ fontSize: 11.5, color: 'var(--ink-3)', marginBottom: 6 }}>{sub}</div>}
      {children}
    </div>
  );
}

type SetForm = React.Dispatch<React.SetStateAction<OnboardForm>>;

function WizardStep1({ form, setForm }: { form: OnboardForm; setForm: SetForm }) {
  return (
    <div data-review="onboarding-step-1">
      <div style={{ fontSize: 18, fontWeight: 600, marginBottom: 4 }}>
        {t('Pháp nhân', 'Business identity')}
      </div>
      <div style={{ fontSize: 12.5, color: 'var(--ink-3)', marginBottom: 20 }}>
        {t(
          'Pháp nhân theo giấy chứng nhận đăng ký kinh doanh. Tên này trở thành tên cơ sở dữ liệu tenant và xuất hiện trên mọi tài liệu audit.',
          'Legal entity per the business registration certificate. This becomes the tenant database name and appears on every audit document.',
        )}
      </div>
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 18 }}>
        <FormField label={t('Tên pháp lý', 'Legal name')}>
          <input
            type="text"
            aria-label={t('Tên pháp lý', 'Legal name')}
            value={form.legal}
            onChange={(e) => setForm((f) => ({ ...f, legal: e.target.value }))}
            style={{ width: '100%' }}
          />
        </FormField>
        <FormField label={t('Khu vực · vùng dữ liệu', 'Region · data residency')}>
          <select
            value={form.region}
            onChange={(e) => setForm((f) => ({ ...f, region: e.target.value }))}
            style={{ width: '100%' }}
            aria-label={t('Khu vực · vùng dữ liệu', 'Region · data residency')}
          >
            <option value="SG-1">SG-1 · ap-southeast-1 · Singapore</option>
            <option value="SG-2">SG-2 · ap-southeast-1b · Singapore (DR)</option>
            <option value="VN-1" disabled>
              VN-1 · planned Q4 2026
            </option>
          </select>
        </FormField>
        <FormField label={t('Loại đăng ký', 'Registration kind')}>
          <select
            value={form.regKind}
            onChange={(e) => setForm((f) => ({ ...f, regKind: e.target.value }))}
            style={{ width: '100%' }}
            aria-label={t('Loại đăng ký', 'Registration kind')}
          >
            <option value="ERC">ERC · Vietnam</option>
            <option value="UEN">UEN · Singapore</option>
            <option value="NIB">NIB · Indonesia</option>
          </select>
        </FormField>
        <FormField label={t('Số đăng ký', 'Registration number')}>
          <input
            type="text"
            aria-label={t('Số đăng ký', 'Registration number')}
            value={form.regNum}
            onChange={(e) => setForm((f) => ({ ...f, regNum: e.target.value }))}
            style={{ width: '100%', fontFamily: 'var(--font-mono)' }}
          />
        </FormField>
        <FormField
          label={t('Gói dịch vụ', 'Tier')}
          sub={t(
            'Quyết định SLO độ trễ đặt chỗ và trần giới hạn tốc độ',
            'Determines reservation latency SLO and rate-limit ceiling',
          )}
        >
          <select
            value={form.tier}
            onChange={(e) => setForm((f) => ({ ...f, tier: e.target.value }))}
            style={{ width: '100%' }}
            aria-label={t('Gói dịch vụ', 'Tier')}
          >
            <option value="Starter">Starter · 5K SKU · 100 đơn/phút</option>
            <option value="Mid-market">Mid-market · 50K SKU · 5K đơn/phút</option>
            <option value="Custom">Tuỳ chỉnh · custom</option>
          </select>
        </FormField>
        <FormField
          label={t('Định danh cơ sở dữ liệu (tự động)', 'Database identifier (auto)')}
          sub={t(
            'Tạo từ slug tên pháp lý, viết thường, tiền tố shopflow_ · tại ap-southeast-1',
            'Generated from legal name slug, lowercased, prefixed with shopflow_ · in ap-southeast-1',
          )}
        >
          <input
            type="text"
            readOnly
            aria-label={t('Định danh cơ sở dữ liệu (tự động)', 'Database identifier (auto)')}
            value={dbNameFromLegal(form.legal)}
            style={{
              width: '100%',
              fontFamily: 'var(--font-mono)',
              background: 'var(--bg-sunken)',
            }}
          />
        </FormField>
      </div>
    </div>
  );
}

function WizardStep2({ form, setForm }: { form: OnboardForm; setForm: SetForm }) {
  return (
    <div data-review="onboarding-step-2">
      <div style={{ fontSize: 18, fontWeight: 600, marginBottom: 4 }}>
        {t('Quản trị viên đầu tiên', 'Initial admin user')}
      </div>
      <div style={{ fontSize: 12.5, color: 'var(--ink-3)', marginBottom: 20 }}>
        {t(
          'Quản trị viên đầu tiên của tenant. Sẽ nhận email kích hoạt một lần và bắt buộc thiết lập TOTP khi đăng nhập lần đầu.',
          'The first administrator of this tenant. Receives a one-time activation email and must set up TOTP on first sign-in.',
        )}
      </div>
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 18 }}>
        <FormField label={t('Họ và tên', 'Full name')}>
          <input
            type="text"
            value={form.adminName}
            onChange={(e) => setForm((f) => ({ ...f, adminName: e.target.value }))}
            style={{ width: '100%' }}
          />
        </FormField>
        <FormField label={t('Email', 'Email')}>
          <input
            type="email"
            value={form.adminEmail}
            onChange={(e) => setForm((f) => ({ ...f, adminEmail: e.target.value }))}
            style={{ width: '100%', fontFamily: 'var(--font-mono)' }}
          />
        </FormField>
        <FormField label={t('Vai trò', 'Role')}>
          <select
            value={form.adminRole}
            onChange={(e) => setForm((f) => ({ ...f, adminRole: e.target.value }))}
            style={{ width: '100%' }}
          >
            <option value="admin">
              {t(
                'Admin · toàn quyền · quản lý người dùng',
                'Admin · full access · manages other users',
              )}
            </option>
            <option value="ops">
              {t(
                'Ops Manager · đọc/ghi tồn kho & đơn',
                'Operations Manager · read/write inventory & orders',
              )}
            </option>
            <option value="seller">
              {t(
                'SME Seller · chỉ đọc KPI + ghi tồn kho',
                'SME Seller · read-only KPIs + write inventory',
              )}
            </option>
          </select>
        </FormField>
        <div />
      </div>
      <div
        style={{
          padding: 12,
          background: 'var(--bg-soft)',
          border: '1px solid var(--line)',
          borderRadius: 3,
          fontSize: 11.5,
          color: 'var(--ink-2)',
          display: 'flex',
          gap: 8,
          alignItems: 'flex-start',
        }}
      >
        <Info
          size={12}
          strokeWidth={1.5}
          style={{ color: 'var(--ink-3)', marginTop: 1, flex: 'none' }}
          aria-hidden
        />
        <span>
          {t(
            'Có thể mời thêm người dùng từ Cài đặt sau khi khởi tạo. Vai trò ánh xạ tới Postgres role theo tenant ( ',
            'Additional users can be invited from Settings after provisioning. Roles map to tenant-scoped Postgres roles ( ',
          )}
          <span className="mono">tenant_admin</span> · <span className="mono">tenant_ops</span> ·{' '}
          <span className="mono">tenant_seller</span>
          {t(' ) — không thể cấp quyền xuyên tenant.', ' ) — no cross-tenant grant is possible.')}
        </span>
      </div>
    </div>
  );
}

function WizardStep3({ form, setForm }: { form: OnboardForm; setForm: SetForm }) {
  const items: { key: SubKey; data: SubProcessor }[] = SUB_KEYS.map((key, i) => ({
    key,
    data: SUBPROCESSORS[i]!,
  }));
  return (
    <div data-review="onboarding-subprocessors">
      <div style={{ fontSize: 18, fontWeight: 600, marginBottom: 4 }}>
        {t('Công bố nhà cung cấp phụ · PDPA', 'Sub-processor disclosure · PDPA')}
      </div>
      <div style={{ fontSize: 12.5, color: 'var(--ink-3)', marginBottom: 20 }}>
        {t(
          'Các dịch vụ này xử lý dữ liệu tenant thay mặt ShopFlow. Mỗi mục phải được xác nhận rõ ràng — không bị giấu trong liên kết Điều khoản dịch vụ. Bạn có thể thu hồi và xuất dữ liệu bất cứ lúc nào từ Cài đặt.',
          "These services process tenant data on ShopFlow's behalf. Each must be explicitly acknowledged — this is not buried in a Terms of Service link. You can revoke and export your data at any time from Settings.",
        )}
      </div>

      <div style={{ border: '1px solid var(--line)', borderRadius: 4, overflow: 'hidden' }}>
        {items.map((it, i) => {
          const checked = form[it.key];
          return (
            <label
              key={it.key}
              style={{
                display: 'grid',
                gridTemplateColumns: '32px 1fr 110px',
                gap: 14,
                alignItems: 'start',
                padding: 14,
                borderBottom: i < items.length - 1 ? '1px solid var(--line)' : 'none',
                cursor: 'pointer',
                background: checked ? 'var(--ok-soft)' : 'transparent',
              }}
            >
              <input
                type="checkbox"
                checked={checked}
                onChange={(e) => setForm((f) => ({ ...f, [it.key]: e.target.checked }))}
                style={{ marginTop: 2, width: 18, height: 18 }}
              />
              <div>
                <div style={{ fontSize: 13, fontWeight: 600 }}>{it.data.name}</div>
                <div className="mono" style={{ fontSize: 11, color: 'var(--ink-3)' }}>
                  {it.data.provider}
                </div>
                <div style={{ fontSize: 12, color: 'var(--ink-2)', marginTop: 4 }}>
                  {t(it.data.purpose.vi, it.data.purpose.en)}
                </div>
              </div>
              <div style={{ textAlign: 'right' }}>
                <div className="lbl" style={{ fontSize: 9.5 }}>
                  {t('Khu vực', 'Region')}
                </div>
                <div className="mono" style={{ fontSize: 12 }}>
                  {it.data.region}
                </div>
                <div className="lbl" style={{ fontSize: 9.5, marginTop: 6 }}>
                  {t('Công bố', 'Disclosed')}
                </div>
                <div className="mono" style={{ fontSize: 11, color: 'var(--ink-3)' }}>
                  {it.data.updated}
                </div>
              </div>
            </label>
          );
        })}
      </div>

      <label
        style={{
          display: 'flex',
          alignItems: 'flex-start',
          gap: 10,
          marginTop: 16,
          padding: 12,
          border: '1px solid var(--accent-line)',
          background: 'var(--accent-soft)',
          borderRadius: 3,
          cursor: 'pointer',
        }}
      >
        <input
          type="checkbox"
          checked={form.pdpa}
          onChange={(e) => setForm((f) => ({ ...f, pdpa: e.target.checked }))}
          style={{ marginTop: 2, width: 18, height: 18 }}
        />
        <div>
          <div style={{ fontSize: 12.5, fontWeight: 600, color: 'var(--accent-ink)' }}>
            {t(
              'Tôi xác nhận thoả thuận xử lý dữ liệu PDPA ở trên',
              'I acknowledge the PDPA processing arrangement above',
            )}
          </div>
          <div style={{ fontSize: 11.5, color: 'var(--ink-2)', marginTop: 2 }}>
            {t(
              `Dữ liệu của tenant này nằm trong một cơ sở dữ liệu Postgres riêng tại ${form.region}. ShopFlow sẽ thông báo cho quản trị viên này ít nhất 30 ngày trước khi thêm hoặc thay đổi bất kỳ nhà cung cấp phụ nào.`,
              `This tenant's data lives in a dedicated Postgres database in ${form.region}. ShopFlow will notify this admin at least 30 days before adding or changing any sub-processor.`,
            )}
          </div>
        </div>
      </label>
    </div>
  );
}

function WizardStep4({
  form,
  demoFail,
  setDemoFail,
}: {
  form: OnboardForm;
  demoFail: boolean;
  setDemoFail: (v: boolean) => void;
}) {
  return (
    <div data-review="onboarding-provision-summary">
      <div style={{ fontSize: 18, fontWeight: 600, marginBottom: 4 }}>
        {t('Sẵn sàng khởi tạo', 'Ready to provision')}
      </div>
      <div style={{ fontSize: 12.5, color: 'var(--ink-3)', marginBottom: 20 }}>
        {t('Nhấn ', 'Click ')}
        <strong>{t('Khởi tạo tenant', 'Provision tenant')}</strong>
        {t(
          ' để tạo cơ sở dữ liệu Postgres riêng, áp dụng migration, đăng ký định tuyến tenant, và chạy kiểm tra sức khoẻ. Thời gian dự kiến ',
          ' to create the dedicated Postgres database, apply migrations, register tenant routing, and run health checks. Expected duration ',
        )}
        <strong>{t('~45 giây', '~45 seconds')}</strong>.
      </div>

      <div className="card">
        <div className="card-h">
          <span className="t">{t('Tóm tắt khởi tạo', 'Provisioning summary')}</span>
        </div>
        <div
          style={{
            padding: 14,
            display: 'grid',
            gridTemplateColumns: '180px 1fr',
            rowGap: 8,
            columnGap: 12,
            fontSize: 12.5,
          }}
        >
          <span style={{ color: 'var(--ink-3)' }}>{t('Pháp nhân', 'Legal entity')}</span>
          <span style={{ fontWeight: 500 }}>{form.legal}</span>
          <span style={{ color: 'var(--ink-3)' }}>{t('Đăng ký', 'Registration')}</span>
          <span className="mono">
            {form.regKind} {form.regNum}
          </span>
          <span style={{ color: 'var(--ink-3)' }}>{t('Khu vực', 'Region')}</span>
          <span className="mono">{form.region} · ap-southeast-1</span>
          <span style={{ color: 'var(--ink-3)' }}>{t('Gói', 'Tier')}</span>
          <span>{form.tier}</span>
          <span style={{ color: 'var(--ink-3)' }}>{t('Cơ sở dữ liệu', 'Database')}</span>
          <span className="mono">{dbNameFromLegal(form.legal)}</span>
          <span style={{ color: 'var(--ink-3)' }}>{t('Quản trị viên', 'Admin user')}</span>
          <span>
            {form.adminName} · <span className="mono">{form.adminEmail}</span>
          </span>
          <span style={{ color: 'var(--ink-3)' }}>{t('Nhà cung cấp phụ', 'Sub-processors')}</span>
          <span>
            {t('4 đã xác nhận · công bố 2026-03-04', '4 acknowledged · disclosure 2026-03-04')}
          </span>
        </div>
      </div>

      <div
        style={{
          marginTop: 14,
          padding: 10,
          background: 'var(--bg-soft)',
          border: '1px solid var(--line)',
          borderRadius: 3,
          fontSize: 11.5,
          color: 'var(--ink-3)',
        }}
      >
        <label style={{ display: 'flex', alignItems: 'center', gap: 8, cursor: 'pointer' }}>
          <input
            type="checkbox"
            checked={demoFail}
            onChange={(e) => setDemoFail(e.target.checked)}
          />
          <span>
            {t(
              'Demo · mô phỏng lỗi tại migration 0007 (để xem trước luồng phục hồi trạng thái một phần)',
              'Demo · simulate failure at migration 0007 (to preview the partial-state recovery flow)',
            )}
          </span>
        </label>
      </div>
    </div>
  );
}

// ── Provisioning live view ───────────────────────────────────────────────────

function Bar({ value, max, kind }: { value: number; max: number; kind?: string }) {
  const pct = Math.max(0, Math.min(100, (value / max) * 100));
  const classes = ['bar', kind].filter(Boolean).join(' ');
  return (
    <div
      className={classes}
      role="progressbar"
      aria-valuenow={Math.round(pct)}
      aria-valuemin={0}
      aria-valuemax={100}
    >
      <i style={{ width: pct + '%' }} />
    </div>
  );
}

function ProvisioningView({
  form,
  progress,
  migCount,
  elapsed,
}: {
  form: OnboardForm;
  progress: ProvProgress;
  migCount: number;
  elapsed: number;
}) {
  return (
    <div className="scroll-y" style={{ flex: 1, padding: '24px 0' }}>
      <div style={{ maxWidth: 880, margin: '0 auto', padding: '0 24px' }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
          <span className="live-dot info" style={{ width: 12, height: 12 }} />
          <div style={{ flex: 1 }}>
            <div style={{ fontSize: 18, fontWeight: 600 }}>
              {t('Đang khởi tạo · ', 'Provisioning · ')}
              {form.legal}
            </div>
            <div className="mono" style={{ fontSize: 11.5, color: 'var(--ink-3)' }}>
              {t('tenant_id đang chờ · vùng ', 'tenant_id pending · region ')}
              {form.region} · saga 0xprov_2026_05_11_017
            </div>
          </div>
          <div style={{ textAlign: 'right' }}>
            <div className="mono tnum" style={{ fontSize: 22, fontWeight: 600 }}>
              {elapsed.toFixed(1)}s
            </div>
            <div style={{ fontSize: 11, color: 'var(--ink-3)' }}>{t('đã trôi qua', 'elapsed')}</div>
          </div>
        </div>

        <div className="card" style={{ marginTop: 18 }} data-review="provisioning-state-machine">
          <div className="card-h">
            <span className="t">
              {t('State machine dòng catalog', 'Catalog row state machine')}
            </span>
            <span style={{ flex: 1 }} />
            <Pill kind="info">{t('đang stream · Tempo trace', 'streaming · Tempo trace')}</Pill>
          </div>
          <div style={{ padding: 0 }}>
            {PROVISIONING_STEPS.map((s, i) => {
              const stt = progress[s.key];
              const isMig = s.key === 'mig';
              return (
                <div
                  key={s.key}
                  style={{
                    display: 'grid',
                    gridTemplateColumns: '40px 1fr 130px 110px',
                    gap: 14,
                    alignItems: 'center',
                    padding: '14px 18px',
                    borderBottom:
                      i < PROVISIONING_STEPS.length - 1 ? '1px solid var(--line)' : 'none',
                    background: stt === 'active' ? 'var(--info-soft)' : 'transparent',
                    opacity: stt === 'pending' ? 0.55 : 1,
                  }}
                >
                  <div style={{ display: 'flex', justifyContent: 'center' }}>
                    <ProvStatusIcon status={stt} />
                  </div>
                  <div>
                    <div style={{ display: 'flex', alignItems: 'baseline', gap: 8 }}>
                      <span style={{ fontSize: 13, fontWeight: 600 }}>
                        {t(s.label.vi, s.label.en)}
                      </span>
                      <span className="mono" style={{ fontSize: 10.5, color: 'var(--accent-ink)' }}>
                        trace {s.traceTail}
                      </span>
                    </div>
                    <div style={{ fontSize: 11.5, color: 'var(--ink-3)', marginTop: 2 }}>
                      {isMig && stt !== 'pending'
                        ? t(
                            `Đã áp dụng ${migCount}/${MIGRATION_TOTAL} migration · checkpoint ${String(migCount).padStart(4, '0')}`,
                            `Applied ${migCount}/${MIGRATION_TOTAL} migrations · checkpoint ${String(migCount).padStart(4, '0')}`,
                          )
                        : t(s.sub.vi, s.sub.en)}
                    </div>
                    {isMig && stt === 'active' && (
                      <div style={{ marginTop: 6, width: 240 }}>
                        <Bar value={migCount} max={MIGRATION_TOTAL} kind="accent" />
                      </div>
                    )}
                  </div>
                  <div style={{ textAlign: 'left' }}>
                    {stt === 'done' && (
                      <span className="mono tnum" style={{ fontSize: 11.5 }}>
                        {(s.latencyMs / 1000).toFixed(2)}s
                      </span>
                    )}
                    {stt === 'active' && <Pill kind="info">{t('đang chạy…', 'running…')}</Pill>}
                    {stt === 'pending' && (
                      <span style={{ fontSize: 11, color: 'var(--ink-4)' }}>
                        {t('chờ', 'queued')}
                      </span>
                    )}
                    {stt === 'fail' && (
                      <span className="mono" style={{ fontSize: 11.5, color: 'var(--bad-ink)' }}>
                        {t('lỗi', 'failed')}
                      </span>
                    )}
                  </div>
                  <div style={{ textAlign: 'right' }}>
                    {stt !== 'pending' && stt !== 'fail' && (
                      <Pill kind={stt === 'done' ? 'ok' : 'info'}>
                        {stt === 'done' ? 'ok' : 'live'}
                      </Pill>
                    )}
                    {stt === 'fail' && (
                      <Pill kind="bad">{t('có thể thử lại', 'retry available')}</Pill>
                    )}
                  </div>
                </div>
              );
            })}
          </div>
        </div>

        <div
          style={{
            marginTop: 14,
            padding: 12,
            background: 'var(--bg-soft)',
            border: '1px solid var(--line)',
            borderRadius: 3,
            fontSize: 11.5,
            color: 'var(--ink-3)',
            display: 'flex',
            gap: 8,
            alignItems: 'center',
          }}
        >
          <Activity size={12} strokeWidth={1.5} style={{ flex: 'none' }} aria-hidden />
          <span>
            {t('Live trace: ', 'Live trace: ')}
            <span className="mono" style={{ color: 'var(--accent-ink)' }}>
              tempo.grafana.shopflow.vn/explore?traceID=0xprov_2026_05_11_017
            </span>
          </span>
        </div>
      </div>
    </div>
  );
}

function ProvStatusIcon({ status }: { status: ProvStatus }) {
  if (status === 'done') {
    return (
      <div
        className="fs0"
        style={{
          width: 22,
          height: 22,
          borderRadius: 11,
          background: 'var(--ok)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
        }}
      >
        <Check size={13} strokeWidth={3} style={{ color: 'var(--ink-inv)' }} aria-hidden />
      </div>
    );
  }
  if (status === 'active') {
    return <span className="live-dot info" style={{ width: 14, height: 14 }} />;
  }
  if (status === 'fail') {
    return (
      <div
        className="fs0"
        style={{
          width: 22,
          height: 22,
          borderRadius: 11,
          background: 'var(--bad)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
        }}
      >
        <AlertTriangle size={13} strokeWidth={3} style={{ color: 'var(--ink-inv)' }} aria-hidden />
      </div>
    );
  }
  return (
    <div
      className="fs0"
      style={{
        width: 22,
        height: 22,
        borderRadius: 11,
        border: '1.5px dashed var(--line-strong)',
      }}
    />
  );
}

// ── Success view ─────────────────────────────────────────────────────────────

function SuccessView({ form, elapsed }: { form: OnboardForm; elapsed: number }) {
  const dbName = dbNameFromLegal(form.legal);
  // Capture the final elapsed once so the figure doesn't drift if a stray
  // re-render lands a fractionally different value.
  const settledRef = useRef<string | null>(null);
  if (settledRef.current === null) settledRef.current = elapsed.toFixed(0);
  const settled = settledRef.current;

  return (
    <div
      data-review="onboarding-success"
      style={{
        flex: 1,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 32,
      }}
    >
      <div style={{ maxWidth: 640 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
          <div
            className="fs0"
            style={{
              width: 38,
              height: 38,
              borderRadius: 19,
              background: 'var(--ok)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            <Check size={20} strokeWidth={3} style={{ color: 'var(--ink-inv)' }} aria-hidden />
          </div>
          <div>
            <div style={{ fontSize: 22, fontWeight: 600, letterSpacing: '-0.01em' }}>
              {t(`Tenant sẵn sàng sau ${settled}s`, `Tenant ready in ${settled}s`)}
            </div>
            <div style={{ fontSize: 12.5, color: 'var(--ink-3)' }}>
              {t('Sẵn sàng · ', 'Ready · ')}
              {form.legal}
            </div>
          </div>
        </div>

        <div className="card" style={{ marginTop: 18 }}>
          <div
            style={{
              padding: 16,
              display: 'grid',
              gridTemplateColumns: '180px 1fr',
              rowGap: 8,
              columnGap: 12,
              fontSize: 12.5,
            }}
          >
            <span style={{ color: 'var(--ink-3)' }}>{t('Cơ sở dữ liệu', 'Database')}</span>
            <span className="mono" style={{ fontWeight: 600 }}>
              {dbName}
            </span>
            <span style={{ color: 'var(--ink-3)' }}>{t('Khu vực', 'Region')}</span>
            <span className="mono">{form.region}</span>
            <span style={{ color: 'var(--ink-3)' }}>{t('Nhà cung cấp phụ', 'Sub-processors')}</span>
            <span>{t('4 · đã xác nhận & công bố', '4 · acknowledged & disclosed')}</span>
            <span style={{ color: 'var(--ink-3)' }}>{t('Migration', 'Migrations')}</span>
            <span className="mono">47/47 · checkpoint 0047</span>
            <span style={{ color: 'var(--ink-3)' }}>{t('Quản trị viên', 'Admin user')}</span>
            <span>
              {form.adminName} · {t('đã gửi email kích hoạt', 'activation email sent')}
            </span>
            <span style={{ color: 'var(--ink-3)' }}>{t('Tempo trace', 'Tempo trace')}</span>
            <span className="mono" style={{ color: 'var(--accent-ink)' }}>
              0xprov_2026_05_11_017
            </span>
          </div>
        </div>

        <div style={{ marginTop: 18, display: 'flex', gap: 10 }}>
          <button className="btn primary lg" type="button">
            <ArrowRight size={13} aria-hidden />{' '}
            {t('Bắt đầu → dashboard', 'Get started → dashboard')}
          </button>
          <button className="btn lg" type="button">
            <Download size={13} aria-hidden />{' '}
            {t('Tải bản ghi khởi tạo', 'Download provisioning record')}
          </button>
        </div>

        <div style={{ marginTop: 22, fontSize: 11.5, color: 'var(--ink-3)' }}>
          {t(
            'Tiếp theo: kết nối một kênh từ màn hình Kênh, rồi chạy import tồn kho lần đầu. Reservation API đang hoạt động · webhook ingest đã đăng ký.',
            'Next: connect a channel from the Channels screen, then run the first inventory import. The reservation API is hot · webhook ingest registered.',
          )}
        </div>
      </div>
    </div>
  );
}

// ── Failure view ─────────────────────────────────────────────────────────────

function FailureView({ onRetry }: { onRetry: () => void }) {
  return (
    <div
      data-review="onboarding-failure"
      style={{
        flex: 1,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 32,
      }}
    >
      <div style={{ maxWidth: 680 }}>
        <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
          <div
            className="fs0"
            style={{
              width: 38,
              height: 38,
              borderRadius: 19,
              background: 'var(--bad)',
              display: 'flex',
              alignItems: 'center',
              justifyContent: 'center',
            }}
          >
            <AlertTriangle
              size={20}
              strokeWidth={2.5}
              style={{ color: 'var(--ink-inv)' }}
              aria-hidden
            />
          </div>
          <div>
            <div style={{ fontSize: 22, fontWeight: 600, letterSpacing: '-0.01em' }}>
              {t('Khởi tạo thất bại tại migration 0007', 'Provisioning failed at migration 0007')}
            </div>
            <div style={{ fontSize: 12.5, color: 'var(--ink-3)' }}>
              {t(
                'Chạy lại an toàn · checkpoint tại migration 0006',
                'Rerun is safe · checkpoint at migration 0006',
              )}
            </div>
          </div>
        </div>

        <div className="card" style={{ marginTop: 18 }}>
          <div
            className="card-h"
            style={{ background: 'var(--bad-soft)', borderBottomColor: 'var(--bad-line)' }}
          >
            <span className="t" style={{ color: 'var(--bad-ink)' }}>
              {t('Chi tiết lỗi', 'Failure details')}
            </span>
          </div>
          <div
            style={{
              padding: 16,
              display: 'grid',
              gridTemplateColumns: '160px 1fr',
              rowGap: 8,
              columnGap: 12,
              fontSize: 12.5,
            }}
          >
            <span style={{ color: 'var(--ink-3)' }}>{t('Bước', 'Step')}</span>
            <span className="mono">migrations · 0007_add_reservation_idem_idx.sql</span>
            <span style={{ color: 'var(--ink-3)' }}>SQLSTATE</span>
            <span className="mono">42P07 · relation "ix_reservation_idem" already exists</span>
            <span style={{ color: 'var(--ink-3)' }}>{t('Checkpoint', 'Checkpoint')}</span>
            <span className="mono">0006 · applied 2026-05-11 14:01:18.302</span>
            <span style={{ color: 'var(--ink-3)' }}>{t('Tempo trace', 'Tempo trace')}</span>
            <span className="mono" style={{ color: 'var(--accent-ink)' }}>
              0xprov_2026_05_11_017 · span migrations
            </span>
            <span style={{ color: 'var(--ink-3)' }}>{t('Trạng thái CSDL', 'Database state')}</span>
            <span>
              {t(
                'Một phần · an toàn để chạy lại (migration idempotent)',
                'Partial · safe to rerun (idempotent migrations)',
              )}
            </span>
          </div>
          <div
            style={{
              padding: 12,
              borderTop: '1px solid var(--line)',
              background: 'var(--bg-soft)',
              fontSize: 11.5,
              color: 'var(--ink-2)',
              display: 'flex',
              gap: 6,
              alignItems: 'flex-start',
            }}
          >
            <Info size={11} strokeWidth={1.5} style={{ marginTop: 2, flex: 'none' }} aria-hidden />
            <span>
              {t(
                'Trình chạy migration dùng mẫu CREATE INDEX IF NOT EXISTS từ 0008 trở đi. Chạy lại sẽ bỏ qua 0001–0006 và thử lại 0007.',
                'Migration runner uses CREATE INDEX IF NOT EXISTS pattern from 0008 onwards. Rerunning will skip 0001–0006 and re-attempt 0007.',
              )}
            </span>
          </div>
        </div>

        <div style={{ marginTop: 18, display: 'flex', gap: 10 }}>
          <button className="btn primary lg" type="button" onClick={onRetry}>
            <RotateCcw size={13} aria-hidden />{' '}
            {t('Thử lại từ checkpoint 0006', 'Retry from checkpoint 0006')}
          </button>
          <button className="btn lg" type="button">
            <Bug size={13} aria-hidden /> {t('Mở Tempo trace', 'Open Tempo trace')}
          </button>
          <button className="btn lg ghost" type="button">
            <Trash2 size={13} aria-hidden />{' '}
            {t('Roll back trạng thái một phần', 'Roll back partial state')}
          </button>
        </div>
      </div>
    </div>
  );
}
