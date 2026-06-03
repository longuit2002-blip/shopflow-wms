import { Fragment, useMemo, useState } from 'react';
import { createFileRoute } from '@tanstack/react-router';
import {
  Pause,
  Play,
  Download,
  ChevronDown,
  ChevronRight,
  Search,
  X,
  Tag,
  User,
  Box,
  Bot,
  ExternalLink,
  Copy,
  Scale,
} from 'lucide-react';
import { Pill } from '../../components/primitives/Pill';
import { t, useLocale } from '../../hooks/useLocale';

/**
 * Audit log — the forensic record (design-handoff wedge screen #2).
 *
 * Ports the design handoff `screen-audit.jsx`. Header strip → filter bar
 * (time range · category multi-select · actor · object type · freeform
 * search across trace_id / idempotency / object id) → compact event table
 * → 560px event-detail drawer (identity · actor & context · before/after
 * JSON diff with a +N −M ~K change-summary header · related events ·
 * compliance reasoning with the lawful-basis article cite).
 *
 * Data is mocked in the frontend (no audit-query backend exists yet — wire
 * to a paginated server query later). `data-review` anchors preserved from
 * the handoff: `idem` (idempotency keys as a first-class column) and
 * `diff-stats` (the JSON-diff change-summary header).
 */

const TENANT_DB = 'shopflow_yensaokhanhhoa';

interface AuditActor {
  id: string;
  name: string;
  role: string;
  email: string;
  init: string;
}

const AUDIT_ACTORS: AuditActor[] = [
  { id: 'u1', name: 'Trần Minh Khôi', role: 'Owner', email: 'khoi@yensaokh.vn', init: 'TK' },
  {
    id: 'u2',
    name: 'Lê Thị Hồng Vân',
    role: 'Ops Manager',
    email: 'van.le@yensaokh.vn',
    init: 'VL',
  },
  {
    id: 'u3',
    name: 'Phạm Văn Đức',
    role: 'Warehouse Op',
    email: 'duc.pham@yensaokh.vn',
    init: 'DP',
  },
  {
    id: 'u4',
    name: 'Nguyễn Hoài Nam',
    role: 'Warehouse Op',
    email: 'nam.nguyen@yensaokh.vn',
    init: 'NN',
  },
  {
    id: 'u5',
    name: 'Đặng Thu Hương',
    role: 'Read-Only',
    email: 'huong.dang@yensaokh.vn',
    init: 'TH',
  },
  { id: 'sys', name: 'System', role: 'saga-orchestrator', email: '—', init: 'SY' },
];

const SYSTEM_ACTOR: AuditActor = AUDIT_ACTORS[AUDIT_ACTORS.length - 1]!;

function aActor(id: string): AuditActor {
  return AUDIT_ACTORS.find((a) => a.id === id) ?? SYSTEM_ACTOR;
}

type DiffData = Record<string, string | number> | 'null';

interface LawfulBasis {
  article: string;
  label: string;
  en: string;
}

interface AuditEvent {
  id: string;
  ts: string;
  date: string;
  cat: string;
  action: string;
  obj: { type: string; id: string };
  actor: string;
  trace: string;
  idem: string;
  ip: string;
  ua: string;
  geo: string;
  session: string;
  saga?: string;
  basis?: LawfulBasis;
  before: DiffData;
  after: DiffData;
  pii?: boolean;
}

const AUDIT_EVENTS: AuditEvent[] = [
  {
    id: 'evt_01HKDX3F7N2',
    ts: '14:23:47.234',
    date: '2026-05-12',
    cat: 'orders',
    action: 'Order state transition',
    obj: { type: 'Order', id: 'SO-2026-05-0042' },
    actor: 'u3',
    trace: 'trc_01HKDX3F4P',
    idem: 'idem_01HKDX_42_p3',
    ip: '10.0.42.18',
    ua: 'iPad Safari 17 · WMS-iOS/0.9.4',
    geo: 'Hồ Chí Minh, VN',
    session: 'sess_d4f1',
    saga: 'saga_01HKDX_4F2',
    basis: {
      article: 'NĐ 13 · Điều 17.1.b',
      label: 'Thực hiện hợp đồng',
      en: 'Contract performance',
    },
    before: { status: '"Picking"', assignee: '"picker_3"', items_picked: 3, packed_at: 'null' },
    after: {
      status: '"Packed"',
      assignee: '"picker_3"',
      items_picked: 5,
      packed_at: '"2026-05-12T14:23:47Z"',
    },
    pii: true,
  },
  {
    id: 'evt_01HKDX3E9K1',
    ts: '14:23:31.012',
    date: '2026-05-12',
    cat: 'orders',
    action: 'PII unmask',
    obj: { type: 'Order', id: 'SO-2026-05-0042' },
    actor: 'u2',
    trace: 'trc_01HKDX3E9Q',
    idem: 'idem_01HKDX_42_unmask',
    ip: '203.116.44.87',
    ua: 'Chrome 124 · macOS 14',
    geo: 'Hồ Chí Minh, VN',
    session: 'sess_a991',
    basis: {
      article: 'NĐ 13 · Điều 17.1.c',
      label: 'Nghĩa vụ pháp lý của bên kiểm soát',
      en: 'Legal obligation of controller',
    },
    before: { customer_name: '"••••••••"', phone: '"••• ••• 384"', address: '"••, Q.3, HCMC"' },
    after: {
      customer_name: '"Nguyễn Thị Lan"',
      phone: '"0907 ••• 384"',
      address: '"27 Lý Tự Trọng, Q.3, HCMC"',
    },
    pii: true,
  },
  {
    id: 'evt_01HKDX3D6M8',
    ts: '14:21:08.557',
    date: '2026-05-12',
    cat: 'inventory',
    action: 'Stock adjusted',
    obj: { type: 'SKU', id: 'YS-TINH-CHE-100G' },
    actor: 'u3',
    trace: 'trc_01HKDX3D6M',
    idem: 'idem_01HKDX_stk_yts_100',
    ip: '10.0.42.18',
    ua: 'iPad Safari 17 · WMS-iOS/0.9.4',
    geo: 'Hồ Chí Minh, VN',
    session: 'sess_d4f1',
    before: { total: 318, reserved: 64, last_count: '"2026-05-11"' },
    after: { total: 312, reserved: 64, last_count: '"2026-05-12"', note: '"cycle count · −6"' },
  },
  {
    id: 'evt_01HKDX3C8Q4',
    ts: '14:18:42.991',
    date: '2026-05-12',
    cat: 'orders',
    action: 'Order state transition',
    obj: { type: 'Order', id: 'SO-2026-05-0041' },
    actor: 'sys',
    trace: 'trc_01HKDX3C8Q',
    idem: 'idem_01HKDX_41_p1',
    ip: '—',
    ua: 'saga-orchestrator/v0.9.4',
    geo: '—',
    session: '—',
    saga: 'saga_01HKDX_41',
    before: { status: '"Reserved"', ledger_step: 1 },
    after: { status: '"Picking"', ledger_step: 2 },
  },
  {
    id: 'evt_01HKDX3B2J7',
    ts: '14:15:23.443',
    date: '2026-05-12',
    cat: 'compliance',
    action: 'Data export requested',
    obj: { type: 'Tenant', id: 'tenant_yensaokh' },
    actor: 'u1',
    trace: 'trc_01HKDX3B2J',
    idem: 'idem_01HKDX_export_apr',
    ip: '113.161.22.4',
    ua: 'Chrome 124 · Windows 11',
    geo: 'Hồ Chí Minh, VN',
    session: 'sess_b001',
    basis: {
      article: 'NĐ 13 · Điều 9.1.a',
      label: 'Quyền truy cập dữ liệu',
      en: 'Right of access',
    },
    before: { last_export: 'null' },
    after: {
      last_export: '"2026-05-12T14:15:23Z"',
      size_mb: 47,
      format: '"sql+json"',
      expires_at: '"2026-05-19T14:15:23Z"',
    },
    pii: true,
  },
  {
    id: 'evt_01HKDX3A1F9',
    ts: '14:12:09.118',
    date: '2026-05-12',
    cat: 'channels',
    action: 'OAuth refreshed',
    obj: { type: 'Channel', id: 'shopee' },
    actor: 'sys',
    trace: 'trc_01HKDX3A1F',
    idem: 'idem_01HKDX_shp_refresh',
    ip: '—',
    ua: 'channel-worker/v0.9.4',
    geo: '—',
    session: '—',
    before: { token_expires_at: '"2026-05-12T14:42:00Z"' },
    after: { token_expires_at: '"2026-05-19T14:12:00Z"', refresh_count: 184 },
  },
  {
    id: 'evt_01HKDX38H4P',
    ts: '14:08:34.667',
    date: '2026-05-12',
    cat: 'users',
    action: 'Member invited',
    obj: { type: 'User', id: 'inv_phong.tran@yensaokh.vn' },
    actor: 'u1',
    trace: 'trc_01HKDX38H4',
    idem: 'idem_01HKDX_inv_phong',
    ip: '113.161.22.4',
    ua: 'Chrome 124 · Windows 11',
    geo: 'Hồ Chí Minh, VN',
    session: 'sess_b001',
    before: 'null',
    after: {
      email: '"phong.tran@yensaokh.vn"',
      role: '"Warehouse Op"',
      expires_at: '"2026-05-19T14:08:34Z"',
    },
  },
  {
    id: 'evt_01HKDX37G2K',
    ts: '14:04:11.290',
    date: '2026-05-12',
    cat: 'settings',
    action: 'Sub-processor list acknowledged',
    obj: { type: 'Setting', id: 'subproc.v12' },
    actor: 'u1',
    trace: 'trc_01HKDX37G2',
    idem: 'idem_01HKDX_subproc_v12',
    ip: '113.161.22.4',
    ua: 'Chrome 124 · Windows 11',
    geo: 'Hồ Chí Minh, VN',
    session: 'sess_b001',
    basis: {
      article: 'NĐ 13 · Điều 13',
      label: 'Công bố nhà cung cấp phụ',
      en: 'Sub-processor disclosure',
    },
    before: { acked_version: 11 },
    after: { acked_version: 12, acked_at: '"2026-05-12T14:04:11Z"' },
  },
  {
    id: 'evt_01HKDX36F8R',
    ts: '14:01:42.218',
    date: '2026-05-12',
    cat: 'orders',
    action: 'Webhook received',
    obj: { type: 'Order', id: 'SO-2026-05-0042' },
    actor: 'sys',
    trace: 'trc_01HKDX36F8',
    idem: 'idem_01HKDX_42_x4f3',
    ip: '13.213.7.42',
    ua: 'shopee-webhook/v3',
    geo: 'AWS · ap-southeast-1',
    session: '—',
    before: 'null',
    after: { source: '"shopee"', payload_size: 4128, signature_valid: 'true' },
  },
  {
    id: 'evt_01HKDX35D1W',
    ts: '13:58:55.119',
    date: '2026-05-12',
    cat: 'inventory',
    action: 'Allocation rule changed',
    obj: { type: 'SKU', id: 'YS-NGUYEN-TO-50G' },
    actor: 'u2',
    trace: 'trc_01HKDX35D1',
    idem: 'idem_01HKDX_alloc_yng50',
    ip: '203.116.44.87',
    ua: 'Chrome 124 · macOS 14',
    geo: 'Hồ Chí Minh, VN',
    session: 'sess_a991',
    before: { shopee: 28, lazada: 18, tiktok: 12, shopify: 4 },
    after: { shopee: 26, lazada: 14, tiktok: 10, shopify: 2 },
  },
  {
    id: 'evt_01HKDX33C9P',
    ts: '13:54:22.041',
    date: '2026-05-12',
    cat: 'tenant',
    action: 'MFA enabled',
    obj: { type: 'User', id: 'duc.pham@yensaokh.vn' },
    actor: 'u3',
    trace: 'trc_01HKDX33C9',
    idem: 'idem_01HKDX_mfa_duc',
    ip: '10.0.42.18',
    ua: 'iPad Safari 17 · WMS-iOS/0.9.4',
    geo: 'Hồ Chí Minh, VN',
    session: 'sess_d4f1',
    before: { mfa: 'false' },
    after: { mfa: 'true', mfa_method: '"TOTP"' },
  },
  {
    id: 'evt_01HKDX32B7Q',
    ts: '13:50:18.881',
    date: '2026-05-12',
    cat: 'api',
    action: 'API key rotated',
    obj: { type: 'API Key', id: 'sf_live_••••_a4b9' },
    actor: 'u1',
    trace: 'trc_01HKDX32B7',
    idem: 'idem_01HKDX_key_rot_a4b9',
    ip: '113.161.22.4',
    ua: 'Chrome 124 · Windows 11',
    geo: 'Hồ Chí Minh, VN',
    session: 'sess_b001',
    before: { last_rotated: '"2026-02-12"', label: '"Production · shopify"' },
    after: { last_rotated: '"2026-05-12T13:50:18Z"', label: '"Production · shopify"' },
  },
  {
    id: 'evt_01HKDX31A4M',
    ts: '13:46:01.337',
    date: '2026-05-12',
    cat: 'inventory',
    action: 'Safety threshold changed',
    obj: { type: 'SKU', id: 'CF-RANG-XAY-500G' },
    actor: 'u2',
    trace: 'trc_01HKDX31A4',
    idem: 'idem_01HKDX_thr_cfr',
    ip: '203.116.44.87',
    ua: 'Chrome 124 · macOS 14',
    geo: 'Hồ Chí Minh, VN',
    session: 'sess_a991',
    before: { threshold: 60 },
    after: { threshold: 80 },
  },
  {
    id: 'evt_01HKDX2Z9L1',
    ts: '13:42:47.553',
    date: '2026-05-12',
    cat: 'orders',
    action: 'Cancellation',
    obj: { type: 'Order', id: 'SO-2026-05-0037' },
    actor: 'u2',
    trace: 'trc_01HKDX2Z9L',
    idem: 'idem_01HKDX_37_cancel',
    ip: '203.116.44.87',
    ua: 'Chrome 124 · macOS 14',
    geo: 'Hồ Chí Minh, VN',
    session: 'sess_a991',
    before: { status: '"Reserved"' },
    after: { status: '"Cancelled"', reason: '"buyer_request"', refund: '"pending"' },
  },
  {
    id: 'evt_01HKDX2Y6K8',
    ts: '13:38:12.880',
    date: '2026-05-12',
    cat: 'tenant',
    action: 'Role changed',
    obj: { type: 'User', id: 'nam.nguyen@yensaokh.vn' },
    actor: 'u1',
    trace: 'trc_01HKDX2Y6K',
    idem: 'idem_01HKDX_role_nam',
    ip: '113.161.22.4',
    ua: 'Chrome 124 · Windows 11',
    geo: 'Hồ Chí Minh, VN',
    session: 'sess_b001',
    before: { role: '"Read-Only"' },
    after: { role: '"Warehouse Op"' },
  },
  {
    id: 'evt_01HKDX2X3J6',
    ts: '13:34:55.114',
    date: '2026-05-12',
    cat: 'orders',
    action: 'Order state transition',
    obj: { type: 'Order', id: 'SO-2026-05-0036' },
    actor: 'sys',
    trace: 'trc_01HKDX2X3J',
    idem: 'idem_01HKDX_36_done',
    ip: '—',
    ua: 'saga-orchestrator/v0.9.4',
    geo: '—',
    session: '—',
    saga: 'saga_01HKDX_36',
    before: { status: '"Packed"' },
    after: { status: '"Shipped"', tracking: '"GHN-VN-9182441"' },
  },
  {
    id: 'evt_01HKDX2W1H3',
    ts: '13:30:42.998',
    date: '2026-05-12',
    cat: 'channels',
    action: 'Mapping updated',
    obj: { type: 'Channel', id: 'tiktok' },
    actor: 'u2',
    trace: 'trc_01HKDX2W1H',
    idem: 'idem_01HKDX_map_tt_yts',
    ip: '203.116.44.87',
    ua: 'Chrome 124 · macOS 14',
    geo: 'Hồ Chí Minh, VN',
    session: 'sess_a991',
    before: { mapped_skus: 412 },
    after: { mapped_skus: 415, last_updated: '"2026-05-12T13:30:42Z"' },
  },
  {
    id: 'evt_01HKDX2V8F1',
    ts: '13:26:18.224',
    date: '2026-05-12',
    cat: 'orders',
    action: 'Webhook received',
    obj: { type: 'Order', id: 'SO-2026-05-0036' },
    actor: 'sys',
    trace: 'trc_01HKDX2V8F',
    idem: 'idem_01HKDX_36_x711',
    ip: '13.213.7.42',
    ua: 'lazada-webhook/v3',
    geo: 'AWS · ap-southeast-1',
    session: '—',
    before: 'null',
    after: { source: '"lazada"', payload_size: 3744, signature_valid: 'true' },
  },
  {
    id: 'evt_01HKDX2U5E7',
    ts: '13:21:09.661',
    date: '2026-05-12',
    cat: 'orders',
    action: 'Refund',
    obj: { type: 'Order', id: 'SO-2026-05-0029' },
    actor: 'u2',
    trace: 'trc_01HKDX2U5E',
    idem: 'idem_01HKDX_29_refund',
    ip: '203.116.44.87',
    ua: 'Chrome 124 · macOS 14',
    geo: 'Hồ Chí Minh, VN',
    session: 'sess_a991',
    basis: {
      article: 'NĐ 13 · Điều 17.1.b',
      label: 'Thực hiện hợp đồng',
      en: 'Contract performance',
    },
    before: { refund_status: '"none"', refund_amount: 0 },
    after: { refund_status: '"completed"', refund_amount: 480000 },
    pii: true,
  },
  {
    id: 'evt_01HKDX2T2D4',
    ts: '13:18:44.107',
    date: '2026-05-12',
    cat: 'tenant',
    action: 'Sign-in',
    obj: { type: 'User', id: 'van.le@yensaokh.vn' },
    actor: 'u2',
    trace: 'trc_01HKDX2T2D',
    idem: 'idem_01HKDX_signin_van',
    ip: '203.116.44.87',
    ua: 'Chrome 124 · macOS 14',
    geo: 'Hồ Chí Minh, VN',
    session: 'sess_a991',
    before: 'null',
    after: { method: '"sso+mfa"', session_id: '"sess_a991"' },
  },
  {
    id: 'evt_01HKDX2S0C9',
    ts: '13:14:21.776',
    date: '2026-05-12',
    cat: 'channels',
    action: 'Channel connected',
    obj: { type: 'Channel', id: 'shopify' },
    actor: 'u1',
    trace: 'trc_01HKDX2S0C',
    idem: 'idem_01HKDX_chn_shopify',
    ip: '113.161.22.4',
    ua: 'Chrome 124 · Windows 11',
    geo: 'Hồ Chí Minh, VN',
    session: 'sess_b001',
    before: { status: '"disconnected"' },
    after: { status: '"connected"', shop_id: '"5728991"', plan: '"basic"' },
  },
  {
    id: 'evt_01HKDX2R7B2',
    ts: '13:09:55.443',
    date: '2026-05-12',
    cat: 'inventory',
    action: 'SKU created',
    obj: { type: 'SKU', id: 'NH-MAT-ONG-500G' },
    actor: 'u2',
    trace: 'trc_01HKDX2R7B',
    idem: 'idem_01HKDX_sku_nmo',
    ip: '203.116.44.87',
    ua: 'Chrome 124 · macOS 14',
    geo: 'Hồ Chí Minh, VN',
    session: 'sess_a991',
    before: 'null',
    after: { name: '"Mật ong rừng nguyên chất 500g"', cat: '"Đặc sản"', total: 92, threshold: 25 },
  },
  {
    id: 'evt_01HKDX2Q4A6',
    ts: '13:05:32.991',
    date: '2026-05-12',
    cat: 'orders',
    action: 'Order state transition',
    obj: { type: 'Order', id: 'SO-2026-05-0034' },
    actor: 'u4',
    trace: 'trc_01HKDX2Q4A',
    idem: 'idem_01HKDX_34_pick',
    ip: '10.0.42.19',
    ua: 'iPad Safari 17 · WMS-iOS/0.9.4',
    geo: 'Hồ Chí Minh, VN',
    session: 'sess_d4f3',
    saga: 'saga_01HKDX_34',
    before: { status: '"Reserved"' },
    after: { status: '"Picking"', assignee: '"picker_4"' },
  },
  {
    id: 'evt_01HKDX2P1Z3',
    ts: '13:01:08.554',
    date: '2026-05-12',
    cat: 'settings',
    action: 'Setting changed',
    obj: { type: 'Setting', id: 'business_hours' },
    actor: 'u1',
    trace: 'trc_01HKDX2P1Z',
    idem: 'idem_01HKDX_bh_update',
    ip: '113.161.22.4',
    ua: 'Chrome 124 · Windows 11',
    geo: 'Hồ Chí Minh, VN',
    session: 'sess_b001',
    before: { saturday_close: '"17:00"' },
    after: { saturday_close: '"12:00"' },
  },
];

interface CatMeta {
  vi: string;
  en: string;
  color: string;
}

const CAT_META: Record<string, CatMeta> = {
  tenant: { vi: 'Tenant', en: 'Tenant', color: '#5a4a3a' },
  users: { vi: 'Người dùng', en: 'Users', color: '#3a5278' },
  channels: { vi: 'Kênh', en: 'Channels', color: '#8a3f3a' },
  inventory: { vi: 'Tồn kho', en: 'Inventory', color: '#6b4a2b' },
  orders: { vi: 'Đơn hàng', en: 'Orders', color: '#5b4878' },
  settings: { vi: 'Cài đặt', en: 'Settings', color: '#3a5a3a' },
  api: { vi: 'API', en: 'API', color: '#4a5a78' },
  compliance: { vi: 'Compliance', en: 'Compliance', color: '#8a6a2b' },
};

function catMeta(c: string): CatMeta {
  return CAT_META[c] ?? { vi: c, en: c, color: '#888' };
}

// ── Route ──────────────────────────────────────────────────────────────────

export const Route = createFileRoute('/_auth/audit')({
  component: AuditRouteComponent,
});

function AuditRouteComponent() {
  useLocale();
  const [follow, setFollow] = useState(false);
  const [selected, setSelected] = useState<string | null>(null);
  const [range, setRange] = useState('24h');
  const [cats, setCats] = useState<Set<string>>(new Set());
  const [actorFilter, setActorFilter] = useState('all');
  const [objType, setObjType] = useState('all');
  const [q, setQ] = useState('');

  const filtered = useMemo(() => {
    return AUDIT_EVENTS.filter((e) => {
      if (cats.size > 0 && !cats.has(e.cat)) return false;
      if (actorFilter !== 'all' && e.actor !== actorFilter) return false;
      if (objType !== 'all' && e.obj.type !== objType) return false;
      if (q) {
        const qq = q.toLowerCase();
        if (
          !(
            e.id.toLowerCase().includes(qq) ||
            e.trace.toLowerCase().includes(qq) ||
            e.idem.toLowerCase().includes(qq) ||
            e.obj.id.toLowerCase().includes(qq)
          )
        )
          return false;
      }
      return true;
    });
  }, [cats, actorFilter, objType, q]);

  const filterCount =
    (cats.size > 0 ? 1 : 0) +
    (actorFilter !== 'all' ? 1 : 0) +
    (objType !== 'all' ? 1 : 0) +
    (q ? 1 : 0) +
    (range !== '24h' ? 1 : 0);

  const selectedEvent = selected ? (AUDIT_EVENTS.find((e) => e.id === selected) ?? null) : null;

  return (
    <div style={{ flex: 1, display: 'flex', flexDirection: 'column', minHeight: 0 }}>
      <div className="strip">
        <span className="t">{t('Audit log', 'Audit log')}</span>
        <span style={{ fontSize: 11.5, color: 'var(--ink-3)' }}>
          ·{' '}
          <span className="mono tnum" style={{ fontWeight: 600 }}>
            3.247
          </span>{' '}
          {t('sự kiện · 24 giờ qua', 'events · last 24h')}
        </span>
        <Pill kind={follow ? 'ok' : 'info'}>
          {follow ? t('đang theo đuôi', 'following tail') : t('tạm dừng', 'paused')}
        </Pill>
        <span style={{ flex: 1 }} />
        <div
          style={{
            display: 'inline-flex',
            border: '1px solid var(--line)',
            borderRadius: 3,
            height: 28,
            overflow: 'hidden',
          }}
        >
          <button
            className="nb"
            type="button"
            onClick={() => setFollow(false)}
            style={{
              padding: '0 10px',
              height: 26,
              border: 'none',
              background: !follow ? 'var(--ink)' : 'transparent',
              color: !follow ? 'var(--ink-inv)' : 'var(--ink-2)',
              fontSize: 11.5,
              fontWeight: 500,
              cursor: 'pointer',
            }}
          >
            <Pause size={10} aria-hidden /> Pause
          </button>
          <button
            className="nb"
            type="button"
            onClick={() => setFollow(true)}
            style={{
              padding: '0 10px',
              height: 26,
              border: 'none',
              borderLeft: '1px solid var(--line)',
              background: follow ? 'var(--ink)' : 'transparent',
              color: follow ? 'var(--ink-inv)' : 'var(--ink-2)',
              fontSize: 11.5,
              fontWeight: 500,
              cursor: 'pointer',
            }}
          >
            <Play size={10} aria-hidden /> {t('Theo đuôi', 'Follow tail')}
          </button>
        </div>
        <button className="btn sm" type="button">
          <Download size={11} strokeWidth={1.5} aria-hidden />{' '}
          {t('Xuất CSV / JSON', 'Export CSV / JSON')} <ChevronDown size={10} aria-hidden />
        </button>
      </div>

      <div
        className="hairline-b"
        style={{
          display: 'flex',
          flexWrap: 'wrap',
          gap: 8,
          rowGap: 8,
          padding: '10px 18px',
          alignItems: 'center',
          background: 'var(--bg-soft)',
        }}
      >
        <div style={{ display: 'flex', gap: 4 }}>
          {['1h', '24h', '7d', '30d', 'Custom'].map((r) => (
            <button
              key={r}
              className={'btn sm' + (range === r ? ' primary' : '')}
              type="button"
              onClick={() => setRange(r)}
              style={{ height: 28 }}
            >
              {r}
            </button>
          ))}
        </div>
        <CategoryMultiSelect cats={cats} setCats={setCats} />
        <ActorSelect value={actorFilter} onChange={setActorFilter} />
        <ObjectTypeSelect value={objType} onChange={setObjType} />
        <div style={{ position: 'relative', flex: '1 1 220px', minWidth: 220 }}>
          <Search
            size={13}
            style={{
              position: 'absolute',
              left: 8,
              top: '50%',
              transform: 'translateY(-50%)',
              color: 'var(--ink-3)',
            }}
            aria-hidden
          />
          <input
            type="search"
            placeholder={t(
              'Tìm trace_id, idempotency, object id…',
              'Search trace_id, idempotency, object id…',
            )}
            style={{
              paddingLeft: 26,
              width: '100%',
              fontFamily: 'var(--font-mono)',
              fontSize: 11.5,
            }}
            value={q}
            onChange={(e) => setQ(e.target.value)}
          />
        </div>
        {filterCount > 0 && (
          <button
            className="btn sm"
            type="button"
            onClick={() => {
              setRange('24h');
              setCats(new Set());
              setActorFilter('all');
              setObjType('all');
              setQ('');
            }}
            style={{ height: 28 }}
          >
            <X size={11} aria-hidden /> {filterCount}{' '}
            {t('bộ lọc đang áp dụng · xoá', 'filters applied · clear')}
          </button>
        )}
      </div>

      <div className="scroll-y" style={{ flex: 1 }}>
        {filtered.length === 0 ? (
          <AuditEmpty />
        ) : (
          <table className="t-data audit">
            <thead>
              <tr>
                <th style={{ width: 160 }}>{t('Thời điểm', 'Timestamp')}</th>
                <th style={{ width: 200 }}>{t('Người thực hiện', 'Actor')}</th>
                <th style={{ width: 230 }}>{t('Hành động', 'Action')}</th>
                <th>Object</th>
                <th style={{ width: 110 }}>Trace</th>
                <th data-review="idem" style={{ width: 130 }}>
                  Idempotency
                </th>
                <th style={{ width: 110 }}>IP</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((e) => {
                const actor = aActor(e.actor);
                const cat = catMeta(e.cat);
                const isSel = selected === e.id;
                return (
                  <tr
                    key={e.id}
                    onClick={() => setSelected(e.id)}
                    className={isSel ? 'sel' : ''}
                    style={{ cursor: 'pointer', height: 32 }}
                  >
                    <td
                      className="mono"
                      style={{ fontSize: 11, color: 'var(--ink-2)', whiteSpace: 'nowrap' }}
                      title={e.date + ' ' + e.ts}
                    >
                      {e.date} {e.ts}
                    </td>
                    <td>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 8, minWidth: 0 }}>
                        {e.actor === 'sys' ? (
                          <div
                            className="fs0"
                            style={{
                              width: 20,
                              height: 20,
                              borderRadius: 10,
                              background: 'var(--bg-sunken)',
                              color: 'var(--ink-3)',
                              display: 'flex',
                              alignItems: 'center',
                              justifyContent: 'center',
                              border: '1px solid var(--line)',
                            }}
                          >
                            <Bot size={11} strokeWidth={1.5} aria-hidden />
                          </div>
                        ) : (
                          <div
                            className="fs0"
                            style={{
                              width: 20,
                              height: 20,
                              borderRadius: 10,
                              background: 'var(--accent-soft)',
                              color: 'var(--accent-ink)',
                              display: 'flex',
                              alignItems: 'center',
                              justifyContent: 'center',
                              fontSize: 9,
                              fontWeight: 700,
                              border: '1px solid var(--accent-line)',
                            }}
                          >
                            {actor.init}
                          </div>
                        )}
                        <span className="tr" style={{ fontSize: 12, fontWeight: 500 }}>
                          {actor.name}
                        </span>
                      </div>
                    </td>
                    <td>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                        <span
                          style={{
                            width: 4,
                            height: 14,
                            background: cat.color,
                            borderRadius: 1,
                            flex: 'none',
                          }}
                        />
                        <span style={{ fontSize: 12, fontWeight: 500 }}>{e.action}</span>
                        {e.pii && <Pill kind="warn">PII</Pill>}
                      </div>
                    </td>
                    <td>
                      <div style={{ display: 'flex', alignItems: 'center', gap: 6, minWidth: 0 }}>
                        <span className="obj-chip">{e.obj.type}</span>
                        <span className="mono tr" style={{ fontSize: 11, color: 'var(--ink-2)' }}>
                          {e.obj.id}
                        </span>
                      </div>
                    </td>
                    <td
                      className="mono"
                      style={{ fontSize: 10.5, color: 'var(--ink-3)' }}
                      title={e.trace}
                    >
                      {e.trace.slice(0, 14)}…
                    </td>
                    <td
                      className="mono"
                      style={{ fontSize: 10.5, color: 'var(--ink-3)' }}
                      title={e.idem}
                    >
                      {e.idem.slice(0, 16)}…
                    </td>
                    <td
                      className="mono"
                      style={{
                        fontSize: 10.5,
                        color: e.ip === '—' ? 'var(--ink-4)' : 'var(--ink-3)',
                      }}
                    >
                      {e.ip}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}
      </div>

      {selectedEvent && (
        <EventDetailDrawer
          event={selectedEvent}
          onClose={() => setSelected(null)}
          onSwap={(id) => setSelected(id)}
        />
      )}
    </div>
  );
}

function CategoryMultiSelect({
  cats,
  setCats,
}: {
  cats: Set<string>;
  setCats: (s: Set<string>) => void;
}) {
  const [open, setOpen] = useState(false);
  const all = Object.keys(CAT_META);
  return (
    <div style={{ position: 'relative' }}>
      <button
        className="btn sm"
        type="button"
        onClick={() => setOpen(!open)}
        style={{ height: 28 }}
      >
        <Tag size={11} aria-hidden />{' '}
        {cats.size === 0
          ? t('Tất cả loại', 'All categories')
          : `${cats.size} ${t('loại', 'types')}`}
        <ChevronDown size={10} aria-hidden />
      </button>
      {open && (
        <div
          style={{
            position: 'absolute',
            top: 32,
            left: 0,
            width: 220,
            background: 'var(--panel)',
            border: '1px solid var(--line)',
            borderRadius: 3,
            boxShadow: 'var(--shadow-pop)',
            zIndex: 10,
            padding: 4,
          }}
        >
          {all.map((c) => {
            const m = catMeta(c);
            const on = cats.has(c);
            return (
              <label
                key={c}
                style={{
                  display: 'flex',
                  alignItems: 'center',
                  gap: 8,
                  padding: '6px 8px',
                  cursor: 'pointer',
                  borderRadius: 2,
                }}
              >
                <input
                  type="checkbox"
                  checked={on}
                  onChange={() => {
                    const n = new Set(cats);
                    if (on) n.delete(c);
                    else n.add(c);
                    setCats(n);
                  }}
                />
                <span
                  style={{
                    width: 4,
                    height: 12,
                    background: m.color,
                    borderRadius: 1,
                    flex: 'none',
                  }}
                />
                <span style={{ fontSize: 12 }}>{t(m.vi, m.en)}</span>
              </label>
            );
          })}
          <div style={{ borderTop: '1px solid var(--line)', padding: 4, marginTop: 4 }}>
            <button
              className="btn sm"
              type="button"
              style={{ width: '100%', justifyContent: 'center' }}
              onClick={() => {
                setCats(new Set());
                setOpen(false);
              }}
            >
              {t('Xoá lựa chọn', 'Clear')}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}

function ActorSelect({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  return (
    <div
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 6,
        padding: '0 8px 0 10px',
        border: '1px solid var(--line)',
        borderRadius: 3,
        height: 28,
        background: 'var(--panel)',
      }}
    >
      <User size={11} strokeWidth={1.5} style={{ color: 'var(--ink-3)' }} aria-hidden />
      <select
        value={value}
        onChange={(e) => onChange(e.target.value)}
        style={{ border: 'none', background: 'transparent', height: 26, fontSize: 12 }}
        aria-label={t('Lọc theo người thực hiện', 'Filter by actor')}
      >
        <option value="all">{t('Tất cả người thực hiện', 'All actors')}</option>
        {AUDIT_ACTORS.map((a) => (
          <option key={a.id} value={a.id}>
            {a.name}
          </option>
        ))}
      </select>
    </div>
  );
}

function ObjectTypeSelect({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const types = ['all', 'Order', 'SKU', 'Tenant', 'User', 'Channel', 'Setting', 'API Key'];
  return (
    <div
      style={{
        display: 'inline-flex',
        alignItems: 'center',
        gap: 6,
        padding: '0 8px 0 10px',
        border: '1px solid var(--line)',
        borderRadius: 3,
        height: 28,
        background: 'var(--panel)',
      }}
    >
      <Box size={11} strokeWidth={1.5} style={{ color: 'var(--ink-3)' }} aria-hidden />
      <select
        value={value}
        onChange={(e) => onChange(e.target.value)}
        style={{ border: 'none', background: 'transparent', height: 26, fontSize: 12 }}
        aria-label={t('Lọc theo loại object', 'Filter by object type')}
      >
        {types.map((tp) => (
          <option key={tp} value={tp}>
            {tp === 'all' ? t('Tất cả object', 'All objects') : tp}
          </option>
        ))}
      </select>
    </div>
  );
}

function AuditEmpty() {
  return (
    <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 60 }}>
      <div style={{ textAlign: 'center', maxWidth: 360 }}>
        <svg
          viewBox="0 0 120 80"
          width="120"
          height="80"
          style={{ display: 'block', margin: '0 auto 14px' }}
          aria-hidden
        >
          <rect
            x="20"
            y="14"
            width="56"
            height="58"
            fill="var(--panel)"
            stroke="var(--line-strong)"
            strokeWidth="1"
          />
          <line x1="28" y1="26" x2="68" y2="26" stroke="var(--line)" strokeWidth="1" />
          <line x1="28" y1="34" x2="60" y2="34" stroke="var(--line)" strokeWidth="1" />
          <line x1="28" y1="42" x2="64" y2="42" stroke="var(--line)" strokeWidth="1" />
          <line x1="28" y1="50" x2="56" y2="50" stroke="var(--line)" strokeWidth="1" />
          <circle cx="78" cy="48" r="14" fill="none" stroke="var(--ink-3)" strokeWidth="2" />
          <line
            x1="89"
            y1="59"
            x2="100"
            y2="70"
            stroke="var(--ink-3)"
            strokeWidth="2.5"
            strokeLinecap="round"
          />
        </svg>
        <div style={{ fontSize: 13, fontWeight: 600 }}>
          {t('Không có sự kiện khớp bộ lọc', 'No events match the filter')}
        </div>
        <div style={{ fontSize: 11.5, color: 'var(--ink-3)', marginTop: 4, lineHeight: 1.5 }}>
          {t(
            'Thử mở rộng khoảng thời gian hoặc xoá một số bộ lọc.',
            'Try expanding the time range or clearing some filters.',
          )}
        </div>
      </div>
    </div>
  );
}

function EventDetailDrawer({
  event,
  onClose,
  onSwap,
}: {
  event: AuditEvent;
  onClose: () => void;
  onSwap: (id: string) => void;
}) {
  const actor = aActor(event.actor);
  const cat = catMeta(event.cat);
  const related = AUDIT_EVENTS.filter(
    (e) =>
      e.id !== event.id &&
      (e.trace === event.trace ||
        (event.saga != null && e.saga === event.saga) ||
        e.actor === event.actor),
  ).slice(0, 5);

  return (
    <Fragment>
      <div className="drawer-mask" onClick={onClose} />
      <div
        className="drawer"
        role="dialog"
        aria-modal="true"
        aria-label={event.action}
        style={{ width: 560 }}
      >
        <div
          style={{
            padding: '14px 18px',
            borderBottom: '1px solid var(--line)',
            display: 'flex',
            alignItems: 'center',
            gap: 10,
          }}
        >
          <span style={{ width: 4, height: 18, background: cat.color, borderRadius: 1 }} />
          <div style={{ flex: 1, minWidth: 0 }}>
            <div style={{ fontSize: 13.5, fontWeight: 600 }}>{event.action}</div>
            <div className="mono" style={{ fontSize: 10.5, color: 'var(--ink-3)' }}>
              {event.date} · {event.ts}
            </div>
          </div>
          {event.pii && <Pill kind="warn">PII</Pill>}
          <button
            className="btn ghost sm"
            type="button"
            onClick={onClose}
            aria-label={t('Đóng', 'Close')}
          >
            <X size={14} aria-hidden />
          </button>
        </div>

        <div className="scroll-y" style={{ flex: 1 }}>
          <DrawerSection title={t('Định danh sự kiện', 'Event identity')}>
            <KV label="Event ID" mono value={event.id} copyable />
            <KV
              label="Trace ID"
              mono
              value={event.trace}
              copyable
              extra={
                <button className="btn sm" type="button" style={{ marginLeft: 6, height: 22 }}>
                  <ExternalLink size={10} aria-hidden /> {t('Mở trong Tempo', 'Open in Tempo')}
                </button>
              }
            />
            <KV
              label="Idempotency"
              mono
              value={event.idem}
              copyable
              hint={t(
                'Khoá idempotency đảm bảo sự kiện chỉ được xử lý đúng một lần. Cùng key = cùng kết quả, an toàn để retry.',
                'Idempotency keys guarantee at-most-once processing. Same key = same outcome, safe to retry.',
              )}
            />
            {event.saga && <KV label="Saga ID" mono value={event.saga} copyable />}
          </DrawerSection>

          <DrawerSection title={t('Người thực hiện · ngữ cảnh', 'Actor & context')}>
            <div style={{ display: 'flex', gap: 10, alignItems: 'center', marginBottom: 10 }}>
              {event.actor === 'sys' ? (
                <div
                  className="fs0"
                  style={{
                    width: 32,
                    height: 32,
                    borderRadius: 16,
                    background: 'var(--bg-sunken)',
                    color: 'var(--ink-3)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    border: '1px solid var(--line)',
                  }}
                >
                  <Bot size={16} aria-hidden />
                </div>
              ) : (
                <div
                  className="fs0"
                  style={{
                    width: 32,
                    height: 32,
                    borderRadius: 16,
                    background: 'var(--accent-soft)',
                    color: 'var(--accent-ink)',
                    display: 'flex',
                    alignItems: 'center',
                    justifyContent: 'center',
                    fontSize: 12,
                    fontWeight: 700,
                    border: '1px solid var(--accent-line)',
                  }}
                >
                  {actor.init}
                </div>
              )}
              <div style={{ flex: 1 }}>
                <div style={{ fontSize: 13, fontWeight: 600 }}>{actor.name}</div>
                <div style={{ fontSize: 11, color: 'var(--ink-3)' }}>
                  {actor.role} · {actor.email}
                </div>
              </div>
            </div>
            <div
              style={{
                display: 'grid',
                gridTemplateColumns: '100px 1fr',
                rowGap: 4,
                columnGap: 12,
                fontSize: 11.5,
              }}
            >
              <div className="lbl">IP</div>
              <div className="mono" style={{ color: 'var(--ink-2)' }}>
                {event.ip} <span style={{ color: 'var(--ink-4)' }}>· {event.geo}</span>
              </div>
              <div className="lbl">User agent</div>
              <div
                className="mono"
                style={{ color: 'var(--ink-2)', fontSize: 10.5 }}
                title={event.ua}
              >
                {event.ua}
              </div>
              <div className="lbl">Session</div>
              <div className="mono" style={{ color: 'var(--ink-2)' }}>
                {event.session}
              </div>
              <div className="lbl">Tenant</div>
              <div className="mono" style={{ color: 'var(--ink-2)' }}>
                {TENANT_DB}
              </div>
            </div>
          </DrawerSection>

          <DrawerSection title={t('Trước / Sau · JSON diff', 'Before / After · JSON diff')}>
            <JsonDiff before={event.before} after={event.after} />
          </DrawerSection>

          {related.length > 0 && (
            <DrawerSection
              title={t('Sự kiện liên quan', 'Related events') + ` · ${related.length}`}
            >
              <div style={{ display: 'flex', flexDirection: 'column', gap: 1 }}>
                {related.map((r) => {
                  const ra = aActor(r.actor);
                  const rc = catMeta(r.cat);
                  return (
                    <button
                      key={r.id}
                      type="button"
                      onClick={() => onSwap(r.id)}
                      className="nb"
                      style={{
                        textAlign: 'left',
                        padding: '8px 10px',
                        background: 'var(--bg-soft)',
                        border: '1px solid var(--line)',
                        borderRadius: 2,
                        cursor: 'pointer',
                        display: 'flex',
                        gap: 10,
                        alignItems: 'center',
                      }}
                    >
                      <span
                        style={{
                          width: 3,
                          height: 24,
                          background: rc.color,
                          borderRadius: 1,
                          flex: 'none',
                        }}
                      />
                      <div style={{ flex: 1, minWidth: 0 }}>
                        <div style={{ fontSize: 12, fontWeight: 500 }}>{r.action}</div>
                        <div className="mono" style={{ fontSize: 10, color: 'var(--ink-3)' }}>
                          {r.ts} · {ra.name} · {r.obj.id}
                        </div>
                      </div>
                      <ChevronRight
                        size={12}
                        strokeWidth={1.5}
                        style={{ color: 'var(--ink-4)' }}
                        aria-hidden
                      />
                    </button>
                  );
                })}
              </div>
            </DrawerSection>
          )}

          {event.basis && (
            <DrawerSection title={t('Cơ sở pháp lý', 'Compliance reasoning')} accent>
              <div style={{ display: 'flex', gap: 10, alignItems: 'flex-start' }}>
                <Scale
                  size={14}
                  strokeWidth={1.5}
                  style={{ color: 'var(--accent-ink)', marginTop: 2 }}
                  aria-hidden
                />
                <div style={{ flex: 1, fontSize: 12, color: 'var(--ink-2)', lineHeight: 1.6 }}>
                  {t(
                    'Sự kiện này liên quan dữ liệu cá nhân. Cơ sở pháp lý xử lý:',
                    'This event touches personal data. Lawful basis for processing:',
                  )}{' '}
                  <span style={{ fontWeight: 600, color: 'var(--ink)' }}>
                    [{t(event.basis.label, event.basis.en)}]
                  </span>{' '}
                  ·{' '}
                  <span className="mono" style={{ fontSize: 11 }}>
                    {event.basis.article}
                  </span>
                  .
                </div>
              </div>
            </DrawerSection>
          )}
        </div>
      </div>
    </Fragment>
  );
}

function DrawerSection({
  title,
  children,
  accent,
}: {
  title: string;
  children: React.ReactNode;
  accent?: boolean;
}) {
  return (
    <div
      style={{
        padding: '14px 18px',
        borderBottom: '1px solid var(--line)',
        background: accent ? 'var(--accent-soft)' : 'transparent',
      }}
    >
      <div className="lbl" style={{ marginBottom: 8 }}>
        {title}
      </div>
      {children}
    </div>
  );
}

function KV({
  label,
  value,
  mono,
  copyable,
  extra,
  hint,
}: {
  label: string;
  value: string;
  mono?: boolean;
  copyable?: boolean;
  extra?: React.ReactNode;
  hint?: string;
}) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 8, padding: '3px 0', minWidth: 0 }}>
      <span className="lbl" style={{ width: 100, flex: 'none' }}>
        {label}
      </span>
      <span
        className={mono ? 'mono' : ''}
        style={{
          fontSize: 11.5,
          color: 'var(--ink)',
          flex: 1,
          minWidth: 0,
          overflow: 'hidden',
          textOverflow: 'ellipsis',
          whiteSpace: 'nowrap',
        }}
        title={hint || value}
      >
        {value}
      </span>
      {copyable && (
        <button
          className="btn ghost sm"
          type="button"
          style={{ height: 22, padding: '0 6px' }}
          title="Copy"
          aria-label={`Copy ${label}`}
          onClick={() => {
            void navigator.clipboard?.writeText(value);
          }}
        >
          <Copy size={10} aria-hidden />
        </button>
      )}
      {extra}
    </div>
  );
}

interface DiffLine {
  ln: number;
  txt: string;
  kind: 'brace' | 'unchanged' | 'removed' | 'added' | 'absent' | 'null';
}

function JsonDiff({ before, after }: { before: DiffData; after: DiffData }) {
  const isObj = (v: DiffData): v is Record<string, string | number> => typeof v === 'object';
  const keys = Array.from(
    new Set([
      ...(isObj(before) ? Object.keys(before) : []),
      ...(isObj(after) ? Object.keys(after) : []),
    ]),
  );

  const renderSide = (obj: DiffData, side: 'before' | 'after'): DiffLine[] => {
    if (!isObj(obj)) {
      return [{ ln: 1, txt: String(obj), kind: 'null' }];
    }
    const lines: DiffLine[] = [{ ln: 1, txt: '{', kind: 'brace' }];
    keys.forEach((k, i) => {
      const v = obj[k];
      const other = side === 'before' ? after : before;
      const otherHas = isObj(other) && k in other;
      const otherVal = isObj(other) ? other[k] : undefined;
      let kind: DiffLine['kind'] = 'unchanged';
      if (v === undefined) kind = 'absent';
      else if (!otherHas) kind = side === 'before' ? 'removed' : 'added';
      else if (String(v) !== String(otherVal)) kind = side === 'before' ? 'removed' : 'added';
      lines.push({
        ln: i + 2,
        txt: v === undefined ? '' : `  "${k}": ${v}${i < keys.length - 1 ? ',' : ''}`,
        kind,
      });
    });
    lines.push({ ln: keys.length + 2, txt: '}', kind: 'brace' });
    return lines;
  };

  const left = renderSide(before, 'before');
  const right = renderSide(after, 'after');

  const stats = (() => {
    const b = isObj(before) ? before : {};
    const a = isObj(after) ? after : {};
    let added = 0;
    let removed = 0;
    let changed = 0;
    let unchanged = 0;
    const all = new Set([...Object.keys(b), ...Object.keys(a)]);
    all.forEach((k) => {
      const inB = k in b;
      const inA = k in a;
      if (inA && !inB) added++;
      else if (inB && !inA) removed++;
      else if (String(b[k]) !== String(a[k])) changed++;
      else unchanged++;
    });
    return { added, removed, changed, unchanged };
  })();

  return (
    <Fragment>
      <div
        data-review="diff-stats"
        style={{
          display: 'flex',
          alignItems: 'center',
          gap: 10,
          padding: '6px 10px',
          marginBottom: 8,
          background: 'var(--bg-soft)',
          border: '1px solid var(--line)',
          borderRadius: 3,
          fontSize: 11,
          fontFamily: 'var(--font-mono)',
        }}
      >
        <span className="lbl" style={{ marginRight: 'auto' }}>
          {t('Tóm tắt thay đổi', 'Change summary')}
        </span>
        <span
          style={{ color: 'var(--ok-ink)', fontWeight: 600 }}
          title={t('Khoá mới được thêm', 'Keys added')}
        >
          +{stats.added}
        </span>
        <span
          style={{ color: 'var(--bad-ink)', fontWeight: 600 }}
          title={t('Khoá bị xoá', 'Keys removed')}
        >
          −{stats.removed}
        </span>
        <span
          style={{ color: 'var(--warn-ink)', fontWeight: 600 }}
          title={t('Khoá có giá trị thay đổi', 'Keys with value change')}
        >
          ~{stats.changed}
        </span>
        <span style={{ color: 'var(--ink-3)' }} title={t('Khoá không đổi', 'Unchanged keys')}>
          · {stats.unchanged} {t('không đổi', 'unchanged')}
        </span>
      </div>
      <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: 8 }}>
        <DiffPane title={t('Trước · before', 'Before')} lines={left} />
        <DiffPane title={t('Sau · after', 'After')} lines={right} />
      </div>
    </Fragment>
  );
}

function DiffPane({ title, lines }: { title: string; lines: DiffLine[] }) {
  return (
    <div
      style={{
        border: '1px solid var(--line)',
        borderRadius: 3,
        background: 'var(--panel)',
        overflow: 'hidden',
      }}
    >
      <div
        style={{
          padding: '6px 10px',
          borderBottom: '1px solid var(--line)',
          background: 'var(--bg-sunken)',
          display: 'flex',
          alignItems: 'center',
        }}
      >
        <span className="lbl">{title}</span>
        <span style={{ flex: 1 }} />
        <button
          className="btn ghost sm"
          type="button"
          style={{ height: 20, padding: '0 6px' }}
          aria-label="Copy"
        >
          <Copy size={10} aria-hidden />
        </button>
      </div>
      <pre
        className="mono"
        style={{ margin: 0, padding: 0, fontSize: 11, lineHeight: 1.6, background: 'var(--panel)' }}
      >
        {lines.map((l, i) => {
          const bg =
            l.kind === 'removed'
              ? 'var(--bad-soft)'
              : l.kind === 'added'
                ? 'var(--ok-soft)'
                : l.kind === 'absent'
                  ? 'var(--bg-sunken)'
                  : 'transparent';
          const fg =
            l.kind === 'removed'
              ? 'var(--bad-ink)'
              : l.kind === 'added'
                ? 'var(--ok-ink)'
                : l.kind === 'brace'
                  ? 'var(--ink-3)'
                  : 'var(--ink)';
          const marker = l.kind === 'removed' ? '-' : l.kind === 'added' ? '+' : ' ';
          return (
            <div key={i} style={{ display: 'flex', background: bg, color: fg, padding: '0 8px' }}>
              <span style={{ width: 22, color: 'var(--ink-4)', userSelect: 'none' }}>{l.ln}</span>
              <span style={{ width: 12, color: 'var(--ink-3)', userSelect: 'none' }}>{marker}</span>
              <span style={{ whiteSpace: 'pre' }}>{l.txt || ' '}</span>
            </div>
          );
        })}
      </pre>
    </div>
  );
}
