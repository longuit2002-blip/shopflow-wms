import { Fragment, type CSSProperties, type ReactNode } from 'react';
import { createFileRoute, Link } from '@tanstack/react-router';
import {
  ArrowRight,
  Database,
  Gauge,
  LayoutGrid,
  Palette,
  ShieldCheck,
  KeySquare,
  Square,
  PackageSearch,
  Compass,
} from 'lucide-react';
import { Pill } from '../components/primitives/Pill';
import { t, useLocale } from '../hooks/useLocale';

/**
 * About / case study — PUBLIC route at `/about`.
 *
 * Ported from the design-handoff `about.html` + `case-study.md`. This is a
 * narrative/marketing surface, NOT app chrome: it lives OUTSIDE the `_auth`
 * shell (no login, no Sidebar/TopBar), as a sibling of `/login`. TanStack
 * Router file-based routing auto-discovers `src/routes/about.tsx` → `/about`.
 *
 * The page is a single readable scroll column (max-width 760px) on the
 * design-system tokens. Bilingual VN/EN where the source carried both;
 * English-only narrative prose is kept English (the source was English),
 * with Vietnamese product / legal / catalog names preserved verbatim.
 *
 * Data (meta grid, design notes, tradeoffs, anchor map) is typed and mapped
 * — no `any`, no record-lookup footguns under noUncheckedIndexedAccess.
 */

// ── Typed content ────────────────────────────────────────────────────────────

interface MetaRow {
  term: string;
  detail: ReactNode;
}

interface NoteMapRow {
  n: string;
  note: string;
  screen: string;
  anchor: string;
  source: string;
}

interface Tradeoff {
  n: string;
  title: string;
  body: ReactNode;
}

interface DesignDecision {
  icon: typeof Palette;
  title: string;
  body: ReactNode;
}

interface Problem {
  n: string;
  icon: typeof Database;
  title: string;
  body: ReactNode;
}

// ── Shared inline styles (kept literal — mirrors about.html typography) ───────
// Declared before the data arrays below, which embed JSX referencing them.
const pStyle: CSSProperties = {
  fontSize: 14,
  lineHeight: 1.65,
  color: 'var(--ink)',
  margin: '0 0 14px',
};
const h2Style: CSSProperties = {
  fontSize: 11,
  fontFamily: 'var(--font-mono)',
  textTransform: 'uppercase',
  letterSpacing: '0.1em',
  color: 'var(--ink-3)',
  fontWeight: 600,
  margin: '56px 0 18px',
  paddingBottom: 8,
  borderBottom: '1px solid var(--line)',
};
const h3Style: CSSProperties = {
  fontSize: 17,
  letterSpacing: '-0.01em',
  fontWeight: 600,
  margin: '28px 0 10px',
};
// Subtle accent emphasis for incumbent names. (Was `ochreStyle` in the amber era;
// renamed — it resolves to --accent-ink, now the indigo accent.)
const accentStyle: CSSProperties = { color: 'var(--accent-ink)', fontWeight: 500 };

const META_ROWS: MetaRow[] = [
  {
    term: t('Đối tượng', 'Audience'),
    detail: t(
      'Nhà bán SME Việt Nam / SEA · 1–5K SKU · 2–5 sàn · một kho',
      'Vietnamese / SEA SME sellers · 1–5K SKUs · 2–5 marketplaces · one warehouse',
    ),
  },
  {
    term: t('Điểm nhọn', 'The wedge'),
    detail: t(
      'Cô lập CSDL vật lý từng tenant — trở thành tính năng mà chủ SME có thể trình bày cho kiểm toán viên, không phải chi tiết hạ tầng giấu trong whitepaper bảo mật',
      'Per-tenant physical database isolation, surfaced as a feature an SME owner can show an auditor — not infra trivia hidden in a security whitepaper',
    ),
  },
  {
    term: t('Ràng buộc', 'Constraint'),
    detail: t(
      'Ba persona (Seller, Ops Manager, Operator) dùng chung một schema nhưng không chung một màn hình',
      'Three personas (Seller, Ops Manager, Operator) sharing one schema but not one screen',
    ),
  },
  {
    term: t('Stack giả định', 'Stack assumed'),
    detail: 'Postgres per-tenant · Outbox · SignalR live channel · ASP.NET Core · ap-southeast-1',
  },
];

const PROBLEMS: Problem[] = [
  {
    n: '1',
    icon: Database,
    title: t(
      'Cô lập CSDL từng tenant như một tính năng UI',
      'Per-tenant database isolation as a UI feature',
    ),
    body: (
      <>
        <p style={pStyle}>
          {t(
            'Mọi B2B SaaS tôi từng dùng đều chôn multi-tenancy như một chi tiết hạ tầng. Tên tenant xuất hiện trong dropdown avatar, có khi trong URL, và hết. ShopFlow đảo ngược điều đó. CSDL từng tenant không phải hạ tầng — nó là một',
            'Every B2B SaaS I’ve used buries multi-tenancy as an infra detail. The tenant name appears in the avatar dropdown, maybe in the URL, and that’s it. ShopFlow inverts this. The per-tenant database isn’t infrastructure — it’s a',
          )}{' '}
          <em>{t('tuyên bố bảo mật hướng người dùng', 'user-facing security statement')}</em>.
        </p>
        <p style={pStyle}>
          {t(
            'Câu trả lời được phân bố khắp sản phẩm. Thanh trên cùng mang khối nhận diện tenant — tên pháp lý, mã ERC',
            'The answer is distributed across the product. The top bar carries a tenant identity block — legal name, ERC number',
          )}{' '}
          <span className="mono">0312445678</span>,{' '}
          {t('vùng HCMC, và tên CSDL', 'region HCMC, and the database name')}{' '}
          <span className="mono">shopflow_yensaokhanhhoa</span>{' '}
          {t(
            'hiển thị bằng monospace, chọn được khi hover. Thấy từ mọi màn hình. Màn Compliance có bản đồ vùng dữ liệu Singapore',
            'rendered in monospace and selectable on hover. Visible from every screen. The Compliance screen has a residency map showing Singapore',
          )}{' '}
          <span className="mono">ap-southeast-1</span>.{' '}
          {t(
            'Audit log đưa idempotency key và trace ID thành cột hạng nhất. Vòng đời tenant được vẽ như một máy trạng thái:',
            'The Audit log surfaces idempotency keys and trace IDs as first-class columns. The tenant lifecycle is drawn as a state machine:',
          )}{' '}
          <span className="mono">
            Active → Suspended → Archive-pending → Archived → DROP DATABASE
          </span>
          {t(
            ', với sàn 485 ngày giữa lần chuyển đầu và cuối.',
            ', with a 485-day floor between the first and last transitions.',
          )}
        </p>
      </>
    ),
  },
  {
    n: '2',
    icon: Gauge,
    title: t(
      'Độ trễ đồng bộ có cận, hiển thị trong workflow vận hành',
      'Bounded sync latency, visible in operator workflow',
    ),
    body: (
      <>
        <p style={pStyle}>
          {t('Tuyên bố kỹ thuật là', 'The engineering claim is')}{' '}
          <span className="mono">p99 &lt; 30s</span>{' '}
          {t(
            'cho lan truyền tồn kho xuyên sàn trong flash sale. Bài toán thiết kế: tuyên bố đó hiện ở đâu trong UI để không chỉ là sales copy?',
            'for stock propagation across channels during flash sales. The design problem: where does that claim show up in the UI so it’s not just sales copy?',
          )}
        </p>
        <p style={pStyle}>
          {t(
            'Ba chỗ. Chấm live-indicator ở thanh trên cùng hiển thị số kết nối SignalR và nhấp nháy theo mỗi broadcast. Khối System health ở sidebar hiển thị p99 độ trễ reservation của',
            'Three places. The live-indicator dot in the top bar shows the SignalR connection count and pulses on each broadcast. The sidebar System health block shows the',
          )}{' '}
          <em>{t('chính tenant đó', 'tenant’s own')}</em>{' '}
          {t('cạnh một pill', 'p99 reservation latency next to a')}{' '}
          <Pill kind="ok">noisy neighbour: stable</Pill>{' '}
          {t('. Và banner cảnh báo vi phạm SLA nằm', 'pill. And the SLA breach banner sits')}{' '}
          <em>{t('phía trên', 'above')}</em>{' '}
          {t(
            'các thẻ doanh thu trên dashboard Ops Manager. Thứ bậc ngầm: nếu có vi phạm đang diễn ra, đó là thứ đầu tiên bạn thấy, trước cả doanh thu hôm nay.',
            'the revenue cards on the Ops Manager dashboard. The implicit hierarchy: if you have a breach in flight, that’s the first thing you see, before today’s gross revenue.',
          )}
        </p>
      </>
    ),
  },
  {
    n: '3',
    icon: LayoutGrid,
    title: t(
      'UI hình-vận-hành, không phải Kanban-người-dùng',
      'Operator-shaped UI, not Kanban-consumer',
    ),
    body: (
      <>
        <p style={pStyle}>
          {t(
            'Đối thủ WMS mặc định bày một bảng Kanban trên màn hình 21-inch vì Kanban chụp ảnh đẹp cho sales deck. Người vận hành kho trên iPad ở zone B-04, đeo găng tay, không muốn Kanban. Họ muốn danh sách pick gom theo zone. Vùng chạm 48px. Số bằng JetBrains Mono để chữ L thường có chấm không bị nhầm với số một dưới ánh đèn huỳnh quang.',
            'The default WMS competitor surfaces a Kanban board on a 21-inch monitor because Kanban photographs well for the sales deck. The warehouse operator on an iPad in zone B-04, wearing gloves, doesn’t want Kanban. They want zone-grouped pick lists. 48px tap targets. Numbers in JetBrains Mono so the dotted lower-case L doesn’t get confused with the digit one under fluorescent light.',
          )}
        </p>
        <p style={pStyle}>
          {t(
            'Bộ chuyển role ở thanh trên cùng không đổi mật độ widget. Nó tráo',
            'The role switcher in the top bar doesn’t change widget density. It swaps the',
          )}{' '}
          <em>{t('màn hình', 'screen')}</em>.{' '}
          {t(
            'Dashboard desktop của Ops Manager không được trình cho operator với font nhỏ hơn. Nó được trình cho một role hoàn toàn khác.',
            'The desktop dashboard for the Ops Manager isn’t shown to the operator with smaller fonts. It’s shown to a different role entirely.',
          )}
        </p>
      </>
    ),
  },
];

const DECISIONS: DesignDecision[] = [
  {
    icon: Palette,
    title: t('Màu là trạng thái, không phải trang trí', 'Color is status, not decoration'),
    body: (
      <p style={pStyle}>
        {t(
          'Bản dựng đầu tiên đi theo mặc định warm-cream + amber-ochre — đúng cái "AI slop" bão hoà của 2025–2026 — và tôi đã gỡ nó. Hệ màu hiện tại là cool/true-neutral: nền gần trắng lạnh, chrome im lặng để dữ liệu dẫn. Màu gần như chỉ tiêu cho hai việc. (1) Trạng thái ngữ nghĩa — ok, cảnh báo, lỗi, thông tin — luôn kèm icon hoặc nhãn, không bao giờ chỉ bằng màu. (2) Nhận diện kênh: badge sàn tự mang màu thương hiệu (Shopee cam, Lazada xanh, TikTok đen, Shopify xanh lá), nên chrome phải trung tính để không đánh nhau với chúng. Accent là một indigo duy nhất',
          'The first build followed the warm-cream + amber-ochre default — exactly the saturated "AI slop" of 2025–2026 — and I removed it. The palette is now cool/true-neutral: a cold near-white body, quiet chrome so the data leads. Color is spent on two things only. (1) Semantic status — ok, warn, bad, info — always paired with an icon or label, never color alone. (2) Channel identity: the marketplace badges carry their own brand colors (Shopee orange, Lazada blue, TikTok near-black, Shopify green), so the chrome has to stay neutral to not fight them. The accent is a single indigo',
        )}{' '}
        <span className="mono" style={{ color: 'var(--accent-ink)', fontWeight: 500 }}>
          #4263EB
        </span>{' '}
        {t(
          'dùng ≤10% cho focus, hành động chính và lựa chọn — một thiết bị đo, không phải brochure.',
          'used ≤10% for focus, primary action, and selection — an instrument, not a brochure.',
        )}
      </p>
    ),
  },
  {
    icon: ShieldCheck,
    title: t(
      'Compliance là một màn hình, không phải link footer',
      'Compliance as a screen, not a footer link',
    ),
    body: (
      <p style={pStyle}>
        {t(
          'Phần lớn SaaS xem compliance như một PDF marketing. Với ShopFlow, điểm nhọn phụ thuộc vào việc làm compliance',
          'Most SaaS treats compliance as a marketing-site PDF. For ShopFlow the wedge depends on making compliance',
        )}{' '}
        <em>{t('chứng minh được trong sản phẩm', 'demonstrable in the product')}</em>
        {t(
          ', nên nó được một màn hình đầy đủ trong cụm Settings, tier-1. Năm phần xếp dọc để kiểm toán viên đi xuống trang một lần: header nhận diện, bản đồ vùng dữ liệu, bảng nhà cung cấp phụ (tám dòng: AWS RDS / ElastiCache / MQ / S3 / Grafana / Sentry / SendGrid / Cloudflare), chính sách lưu giữ với trích dẫn điều luật Việt Nam thực tế (',
          ', so it gets a full screen in the Settings cluster, tier-1, marquee depth. Five sections stacked vertically so an auditor walks down the page once: identity header, data residency map, sub-processor table (eight rows: AWS RDS / ElastiCache / MQ / S3 / Grafana / Sentry / SendGrid / Cloudflare), retention with actual Vietnamese legal article citations (',
        )}
        <span className="mono">NĐ 13 · §17</span>
        {t(
          ', Luật Kế toán cho 7 năm lưu giữ kế toán), máy trạng thái vòng đời.',
          ', Luật Kế toán for the 7-year accounting retention), lifecycle state machine.',
        )}
      </p>
    ),
  },
  {
    icon: KeySquare,
    title: t('Idempotency key như cột hạng nhất', 'Idempotency keys as first-class columns'),
    body: (
      <p style={pStyle}>
        {t(
          'Bảng Audit log có sáu cột: timestamp, actor, action, object, trace, idempotency. Cột trace và idempotency dùng JetBrains Mono, cắt còn 14 ký tự với dấu ba chấm. Click bất kỳ dòng nào và drawer chi tiết hiện đầy đủ key với nút copy, một link “Open in Tempo” cạnh trace ID, và một tooltip trên idempotency key giải thích rằng',
          'The Audit log table has six columns: timestamp, actor, action, object, trace, idempotency. The trace and idempotency columns are JetBrains Mono, truncated to 14 characters with an ellipsis. Click any row and the detail drawer shows the full keys with copy buttons, an “Open in Tempo” link next to the trace ID, and a tooltip on the idempotency key explaining that',
        )}{' '}
        <em>
          {t(
            'cùng một key trả về cùng một kết quả — an toàn để retry',
            'the same key returns the same outcome — safe to retry',
          )}
        </em>
        .{' '}
        {t(
          'Chi tiết cuối đó là thứ nói với một kỹ sư senior đọc case study này rằng người thiết kế thực sự hiểu ngữ nghĩa at-most-once mà họ đang phơi bày. Idempotency key không phải để khoe. Nó là một dấu hiệu.',
          'That last detail is what tells a senior engineer reading this case study that the designer actually understands the at-most-once semantics they’re surfacing. Idempotency keys aren’t a flex. They’re a tell.',
        )}
      </p>
    ),
  },
  {
    icon: Square,
    title: t('Đường viền, không phải đổ bóng', 'Borders, not shadows'),
    body: (
      <p style={pStyle}>
        {t(
          'Bloomberg gặp Linear — cả hai dựa vào đường viền',
          'Bloomberg meets Linear — both products lean on',
        )}{' '}
        <span className="mono">1px solid</span>{' '}
        {t('để phân tách thay vì độ cao bằng', 'borders for separation rather than')}{' '}
        <span className="mono">box-shadow</span>{' '}
        {t(
          'elevation. Đường viền sống sót qua zoom, in ấn, và chế độ tương phản cao; chúng không nhoè ở độ lệch sub-pixel; chúng không đánh nhau với dữ liệu bảng như bóng đổ đánh nhau với chiều cao dòng chật. Bóng đổ vẫn tồn tại, nhưng chỉ trên ba thứ: popover chuyển tenant, dropdown thông báo, và drawer chi tiết audit-event. Ba là một danh sách đủ ngắn để nhớ; nếu nó trườn tới mười thì kỷ luật đã hỏng.',
          'elevation. Borders survive zoom, print, and high-contrast mode; they don’t smear at sub-pixel offsets; they don’t fight tabular data the way drop shadows fight a tight row height. Shadows exist, but only on three things: the tenant-switcher popover, the notifications dropdown, and the audit-event detail drawer. Three is a small enough list to remember; if it creeps to ten the discipline has failed.',
        )}
      </p>
    ),
  },
  {
    icon: PackageSearch,
    title: t(
      'Yến sào / Cà phê / Đặc sản, không phải apparel / beverages / food',
      'Yến sào / Cà phê / Đặc sản, not apparel / beverages / food',
    ),
    body: (
      <p style={pStyle}>
        {t(
          'Danh mục chung chung là dấu hiệu người thiết kế bịa data trong hai phút. Catalog là tiếng Việt: Yến sào tinh chế 100g, Cà phê rang xay 500g, Mật ong rừng nguyên chất 500g — SKU ánh xạ tới doanh nghiệp SME thật. Tenant là Yến Sào Khánh Hoà Co., Ltd, do Trần Minh Khôi điều hành. Bảng quản trị tenant liệt kê 12 tenant trải sáu trạng thái vòng đời. Mock data là nơi rẻ nhất để hoặc chứng minh hoặc đánh chìm uy tín domain.',
          'Generic categories are the giveaway that the designer mocked data in two minutes. The catalog is Vietnamese: Yến sào tinh chế 100g, Cà phê rang xay 500g, Mật ong rừng nguyên chất 500g — SKUs that map to actual SME businesses. The tenant is Yến Sào Khánh Hoà Co., Ltd, run by Trần Minh Khôi. The tenant admin lists 12 tenants across six lifecycle states. Mock data is the cheapest place to either prove or torpedo domain credibility.',
        )}
      </p>
    ),
  },
];

const TRADEOFFS: Tradeoff[] = [
  {
    n: '01',
    title: t('Các API là giả lập.', 'The APIs are mocked.'),
    body: t(
      'Shopee, Lazada, TikTok Shop, Shopify là mock hướng-sự-kiện. Một bản thật sẽ xử lý riêng quirk rate-limit của từng nền (bucket per-shop của Shopee, throttle gắt hơn của TikTok, leaky-bucket của Shopify), ngữ nghĩa retry webhook khác nhau từng nền, và bốn pattern refresh OAuth khác nhau.',
      'Shopee, Lazada, TikTok Shop, Shopify are event-driven mocks. A real implementation would handle each platform’s rate-limit quirks separately (Shopee’s per-shop bucket, TikTok’s stricter throttling, Shopify’s leaky-bucket), webhook retry semantics that differ per platform, and four different OAuth refresh patterns.',
    ),
  },
  {
    n: '02',
    title: t('Chưa có user testing.', 'No user testing yet.'),
    body: t(
      'Đây là prototype portfolio, không phải sản phẩm đã kiểm chứng. Kế hoạch là biến nó thành research probe cho 8 phỏng vấn nhà bán SME — chạy bộ chuyển role với từng người, xem họ ngập ngừng ở đâu, ghi lại các tác vụ họ thử làm mà sản phẩm không hỗ trợ. Chưa điều nào trong số đó xảy ra.',
      'This is a portfolio prototype, not a validated product. The plan is to convert this into a research probe for 8 SME-seller interviews — running the role switcher with each interviewee, watching where they hesitate, recording the tasks they try to do that the product doesn’t support. None of that has happened.',
    ),
  },
  {
    n: '03',
    title: t('Compliance là giả lập.', 'Compliance is mocked.'),
    body: (
      <>
        {t(
          'Các màn hình trông đúng và dùng tham chiếu luật Việt Nam thật, nhưng luồng xuất dữ liệu không thực sự tạo SQL dump, và modal xoá tenant không thực sự lên lịch một',
          'The screens look right and use real Vietnamese legal references, but the data export flow doesn’t produce a SQL dump, and the tenant deletion modal doesn’t actually schedule a',
        )}{' '}
        <span className="mono">DROP DATABASE</span>
        {t(
          '. Một bản đã ship sẽ cần một audit-outbox pipeline thật, vòng đời lưu trữ 90 ngày như một máy trạng thái hoạt động, và công bố nhà cung cấp phụ có phiên bản.',
          '. A shipped version would need a real audit-outbox pipeline, the 90-day archive lifecycle as a working state machine, and versioned sub-processor disclosures.',
        )}
      </>
    ),
  },
];

const NOTE_MAP: NoteMapRow[] = [
  {
    n: '01',
    note: t('Màu là trạng thái, không phải trang trí', 'Color is status, not decoration'),
    screen: 'Inventory',
    anchor: '[data-review="palette"]',
    source: 'screen-inventory.jsx',
  },
  {
    n: '02',
    note: t('Nhận diện tenant trên mọi màn hình', 'Tenant identity on every screen'),
    screen: 'Dashboard',
    anchor: '[data-tour="tenant-pill"]',
    source: 'components.jsx · TopBar',
  },
  {
    n: '03',
    note: t('Phơi bày vùng CSDL từng tenant', 'Per-tenant DB residency surfaced'),
    screen: 'Compliance',
    anchor: '[data-review="residency"]',
    source: 'screen-compliance.jsx',
  },
  {
    n: '04',
    note: t('Saga ledger như xương sống forensic', 'Saga ledger as forensic spine'),
    screen: 'Orders',
    anchor: '[data-review="saga"]',
    source: 'screen-orders.jsx',
  },
  {
    n: '05',
    note: t('Idempotency key như cột', 'Idempotency keys as columns'),
    screen: 'Audit',
    anchor: '[data-review="idem"]',
    source: 'screen-audit.jsx',
  },
  {
    n: '06',
    note: t('Nội dung Việt, không dịch máy', 'Vietnamese content, not translated'),
    screen: 'Inventory',
    anchor: '[data-review="vn-content"]',
    source: 'screen-inventory.jsx · data.jsx',
  },
  {
    n: '07',
    note: t('Đường viền, không phải bóng đổ', 'Borders, not shadows'),
    screen: 'Dashboard',
    anchor: '[data-review="border-card"]',
    source: 'screen-dashboard.jsx',
  },
  {
    n: '08',
    note: t('< 1024px hiện thông báo, không responsive', '< 1024px shows notice, no responsive'),
    screen: 'Dashboard',
    anchor: '[data-tour="live-indicator"]',
    source: 'components.jsx · TopBar',
  },
  {
    n: '09',
    note: t('Nhà cung cấp phụ kèm vùng + ngày DPA', 'Sub-processors with region + DPA date'),
    screen: 'Compliance',
    anchor: '[data-review="subprocessors"]',
    source: 'screen-compliance.jsx',
  },
  {
    n: '10',
    note: t('Empty state như thiết kế có chủ đích', 'Empty state as deliberate design'),
    screen: 'Inventory · Orders',
    anchor: '[data-review="empty"]',
    source: 'screen-inventory.jsx · screen-orders.jsx',
  },
];

// (shared inline styles — pStyle / h2Style / h3Style / accentStyle — are
// declared above the data arrays near the top of this file.)

// ── Route ────────────────────────────────────────────────────────────────────

export const Route = createFileRoute('/about')({
  component: AboutRouteComponent,
});

function AboutRouteComponent() {
  useLocale();
  return (
    <div
      className="scroll-y"
      style={{ height: '100%', background: 'var(--bg)', color: 'var(--ink)' }}
    >
      <div style={{ maxWidth: 760, margin: '0 auto', padding: '64px 32px 96px' }}>
        <TopBar />

        <h1
          style={{
            fontSize: 30,
            letterSpacing: '-0.02em',
            lineHeight: 1.15,
            fontWeight: 700,
            margin: '0 0 14px',
            textWrap: 'balance',
          }}
        >
          {t(
            'Thiết kế một B2B SaaS nơi cô lập dữ liệu là một tính năng sản phẩm.',
            'Designing a B2B SaaS where data isolation is a product feature.',
          )}
        </h1>
        <p style={{ fontSize: 16, lineHeight: 1.6, color: 'var(--ink-2)', margin: '0 0 18px' }}>
          {t(
            'ShopFlow WMS là một SaaS quản lý kho đa-tenant cho nhà bán SME Việt Nam, xây quanh tuyên bố rằng bạn có thể đưa kiểm toán viên một câu lệnh SQL và để họ tự xác minh việc cô lập dữ liệu của tenant trong 30 giây. Bài toán thiết kế thú vị không phải làm một dashboard kho tốt hơn — mà là làm cho cam kết cô lập đó hiện ',
            'ShopFlow WMS is a multi-tenant warehouse management SaaS for Vietnamese SME sellers, built around the claim that you can hand an auditor a SQL prompt and let them verify your tenant’s data isolation in 30 seconds. The interesting design problem wasn’t building a better warehouse dashboard — it was making that isolation guarantee visible ',
          )}
          <em>{t('bên trong', 'inside')}</em>
          {t(
            ' sản phẩm, không chỉ trên một trang bán hàng.',
            ' the product, not just on a sales page.',
          )}
        </p>
        <div
          className="mono"
          style={{ fontSize: 11, color: 'var(--ink-3)', letterSpacing: '0.04em', marginBottom: 36 }}
        >
          {t(
            'đọc 90 giây · 5 màn · 3 role · 10 design notes',
            '90-second read · 5 screens · 3 roles · 10 design notes',
          )}
        </div>

        <MetaGrid />

        <h2 style={h2Style}>{t('Bài toán', 'The problem')}</h2>
        <p style={pStyle}>
          {t(
            'Nhà bán SME Việt Nam có một “stack”. Một file Excel trong bản dùng thử Microsoft 365 miễn phí. Một tab Shopee Seller Center. Một tab Lazada Seller Center. Một tab TikTok Shop, thường trên điện thoại vì công cụ desktop của TikTok là thứ làm cho có. Họ đối chiếu tồn kho thủ công qua các bề mặt này, hai lần một ngày — nhiều hơn trong flash sale.',
            'The Vietnamese SME seller has a stack. Excel spreadsheet in a free Microsoft 365 trial. A Shopee Seller Center tab. A Lazada Seller Center tab. A TikTok Shop tab, usually on a phone because TikTok’s desktop tooling is an afterthought. They reconcile inventory across these surfaces by hand, twice a day — more during flash sales.',
          )}
        </p>
        <p style={pStyle}>
          {t(
            'Các đối thủ hiện hữu là sản phẩm thật, đang chạy.',
            'The incumbents are real, working products.',
          )}{' '}
          <span style={accentStyle}>Sapo</span>{' '}
          {t(
            'cho gom đơn hợp nhất nhưng phép tính tồn kho đa-kênh chỉ best-effort — trong 11.11 cảnh báo oversell sáng đèn theo thời gian thực và bạn không thể dừng.',
            'gives unified order intake but its multi-channel inventory math is best-effort — during 11.11 oversell warnings light up in real time and you can’t stop them.',
          )}{' '}
          <span style={accentStyle}>Haravan</span>{' '}
          {t(
            'mạnh về dựng website thương mại nhưng lớp WMS mỏng hơn lớp storefront; người vận hành kho lại quay về Excel.',
            'is strong on commerce-site building but its WMS layer is thinner than its storefront layer; warehouse operators end up back in Excel.',
          )}{' '}
          <span style={accentStyle}>KiotViet</span>{' '}
          {t(
            'có DNA POS sâu nhất và tích hợp hoá đơn thuế Việt tốt nhất, nhưng nó được xây cho cửa hàng vật lý với một tab marketplace thêm vào sau.',
            'has the deepest POS DNA and best Vietnamese tax-invoice integration, but it was built for brick-and-mortar with a marketplace tab added later.',
          )}
        </p>
        <p style={pStyle}>
          {t(
            'Trong flash sale — 11.11, 12.12, 3.3 — kiểu lỗi nhất quán. Một người bán oversell đúng 12 đơn vị cho cả Shopee và Lazada trong vòng bốn phút sau khi mở campaign. Họ huỷ nửa số đơn, gánh hoàn tiền, bị tụt hạng đối tác Shopee Mall, mất ba tuần xây lại điểm seller-rating. Cái giá không phải lượng hoàn tiền. Đó là trạng thái đối tác.',
            'During flash sales — 11.11, 12.12, 3.3 — the failure mode is consistent. A seller oversells the same 12 units to Shopee and Lazada within four minutes of campaign launch. They cancel half the orders, eat the refunds, get demoted on Shopee Mall partnership tier, spend three weeks rebuilding their seller-rating score. The cost isn’t the refund volume. It’s the partnership status.',
          )}
        </p>
        <p style={pStyle}>
          {t('Và năm 2023,', 'And in 2023,')} <span className="mono">Decree 13/2023/NĐ-CP</span>{' '}
          {t(
            'nâng sàn bảo vệ dữ liệu cho mọi SaaS xử lý dữ liệu cá nhân của người Việt. Đối tác Shopee Mall ngày càng đối mặt áp lực kiểm toán mà SaaS đa-tenant logic (chung schema,',
            'raised the data-protection floor for any SaaS handling Vietnamese personal data. Shopee Mall partners increasingly face audit pressure that logically-multi-tenant SaaS (shared schema,',
          )}{' '}
          <code className="mono">WHERE tenant_id = ?</code>{' '}
          {t(
            'khắp nơi) không thể vượt qua sạch sẽ — không phải vì phép tính sai, mà vì câu chuyện chứng minh khó trình bày trong một buổi kiểm toán 30 phút.',
            'everywhere) can’t pass cleanly — not because the math is wrong, but because the proof story is hard to demonstrate in a 30-minute auditor walkthrough.',
          )}
        </p>
        <Blockquote>
          {t(
            'Vậy bài toán thiết kế không phải “một dashboard kho tốt hơn”. Mà là “làm sao xây một B2B SaaS nơi cam kết cô lập dữ liệu là một tính năng sản phẩm mà chủ SME có thể trình cho kiểm toán viên trong 30 giây?”',
            'So the design problem wasn’t “a better warehouse dashboard.” It was “how do you build a B2B SaaS where the data-isolation guarantee is a product feature an SME owner can show an auditor in 30 seconds?”',
          )}
        </Blockquote>

        <h2 style={h2Style}>{t('Ba bài toán thiết kế', 'Three design problems')}</h2>
        {PROBLEMS.map((p) => (
          <ProblemBlock key={p.n} problem={p} />
        ))}

        <h2 style={h2Style}>{t('Một số quyết định thiết kế', 'Selected design decisions')}</h2>
        {DECISIONS.map((d) => (
          <DecisionBlock key={d.title} decision={d} />
        ))}

        <h2 style={h2Style}>
          {t('Ba đánh đổi tôi nói thẳng', 'Three tradeoffs I’m explicit about')}
        </h2>
        <TradeoffList />

        <h2 style={h2Style}>{t('Tiếp theo là gì', 'What’s next')}</h2>
        <p style={pStyle}>
          {t(
            'Ba việc, theo thứ tự. Thứ nhất, biến artifact thành một research probe có thể click được và chạy tám phỏng vấn nhà bán SME — không phải user-test UI, mà dùng UI như một bài kiểm tra từ vựng xem điểm nhọn có cộng hưởng với đúng người nó nhắm tới không. Thứ hai, thiết kế khoảnh khắc kích hoạt Ngày-1 giữa lúc hoàn tất Onboarding tenant và lần kết nối kênh đầu tiên — hiện wizard onboarding bàn giao cho một dashboard trống, sai cảm giác cho 90 giây đầu của một sản phẩm trả tiền. Thứ ba, tách lớp component thành một Storybook để chứng minh design system tái sử dụng được.',
            'Three things, in order. First, convert the artifact into a clickable research probe and run eight SME-seller interviews — not user-testing the UI, but using the UI as a vocabulary check on whether the wedge resonates with the people it’s meant for. Second, design the Day-1 activation moment between Tenant Onboarding completion and the first channel connection — right now the onboarding wizard hands off to an empty dashboard, which is the wrong feeling for the first 90 seconds of a paid product. Third, extract the component layer into a Storybook to prove the design system is reusable.',
          )}
        </p>

        <h2 style={h2Style}>
          {t('10 design notes · tìm chúng ở đâu', '10 design notes · where to find them')}
        </h2>
        <p style={pStyle}>
          {t(
            'Mỗi tuyên bố trong case study được neo vào một DOM node cụ thể trong prototype, để một reviewer (hoặc một agent đọc code) có thể xác minh từng cái trong < 30 giây. Guided tour quay vòng chúng theo thứ tự.',
            'Every claim in the case study is anchored to a specific DOM node in the prototype, so a reviewer (or a code-reading agent) can verify each one in < 30 seconds. The guided tour cycles these in order.',
          )}
        </p>
        <NotesMap />
        <p
          style={{
            fontSize: 12.5,
            color: 'var(--ink-3)',
            margin: '6px 0 22px',
            padding: '10px 12px',
            background: 'var(--bg-soft)',
            borderLeft: '2px solid var(--accent-ink)',
          }}
        >
          {t(
            'Bề mặt forensic bổ sung thêm muộn: tóm tắt thay đổi JSON diff trong audit drawer — ',
            'Bonus forensic surface added late: JSON diff change-summary in the audit drawer — ',
          )}
          <span className="mono">[data-review="diff-stats"]</span> {t('trong', 'in')}{' '}
          <span className="mono">screen-audit.jsx</span>.
        </p>

        <PullQuote />

        <Footer />
      </div>
    </div>
  );
}

// ── Sub-components ─────────────────────────────────────────────────────────────

function TopBar() {
  return (
    <header
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 12,
        paddingBottom: 24,
        borderBottom: '1px solid var(--line)',
        marginBottom: 48,
      }}
    >
      <span style={{ fontWeight: 700, letterSpacing: '-0.01em', fontSize: 14 }}>ShopFlow WMS</span>
      <span
        className="mono"
        style={{ flex: 1, fontSize: 11, color: 'var(--ink-3)', letterSpacing: '0.04em' }}
      >
        {t('portfolio · case study · 2026', 'portfolio · case study · 2026')}
      </span>
      <Link
        to="/login"
        className="btn sm"
        style={{ textDecoration: 'none' }}
        data-testid="about-enter-app"
      >
        {t('Vào ứng dụng', 'Enter the app')} <ArrowRight size={12} strokeWidth={1.5} aria-hidden />
      </Link>
    </header>
  );
}

function MetaGrid() {
  return (
    <dl
      style={{
        display: 'grid',
        gridTemplateColumns: '110px 1fr',
        gap: '6px 18px',
        fontSize: 13,
        lineHeight: 1.55,
        margin: '18px 0 24px',
      }}
    >
      {META_ROWS.map((row) => (
        <Fragment key={row.term}>
          <dt
            className="mono"
            style={{ color: 'var(--ink-3)', fontSize: 11, letterSpacing: '0.04em', paddingTop: 2 }}
          >
            {row.term}
          </dt>
          <dd style={{ margin: 0 }}>{row.detail}</dd>
        </Fragment>
      ))}
    </dl>
  );
}

function Blockquote({ children }: { children: ReactNode }) {
  return (
    <blockquote
      style={{
        borderLeft: '2px solid var(--accent-ink)',
        margin: '22px 0',
        padding: '4px 0 4px 18px',
        fontSize: 15,
        color: 'var(--ink-2)',
        fontStyle: 'italic',
        lineHeight: 1.55,
      }}
    >
      {children}
    </blockquote>
  );
}

function ProblemBlock({ problem }: { problem: Problem }) {
  const IconCmp = problem.icon;
  return (
    <div>
      <h3 style={{ ...h3Style, display: 'flex', alignItems: 'center', gap: 9 }}>
        <span
          className="fs0 mono"
          style={{
            width: 22,
            height: 22,
            borderRadius: 4,
            background: 'var(--accent-soft)',
            color: 'var(--accent-ink)',
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            fontSize: 11,
            fontWeight: 700,
          }}
        >
          {problem.n}
        </span>
        <IconCmp size={16} strokeWidth={1.5} style={{ color: 'var(--ink-3)' }} aria-hidden />
        <span>{problem.title}</span>
      </h3>
      {problem.body}
    </div>
  );
}

function DecisionBlock({ decision }: { decision: DesignDecision }) {
  const IconCmp = decision.icon;
  return (
    <div>
      <h3 style={{ ...h3Style, display: 'flex', alignItems: 'center', gap: 9 }}>
        <IconCmp size={16} strokeWidth={1.5} style={{ color: 'var(--accent-ink)' }} aria-hidden />
        <span>{decision.title}</span>
      </h3>
      {decision.body}
    </div>
  );
}

function TradeoffList() {
  return (
    <ul style={{ listStyle: 'none', padding: 0, margin: '12px 0 22px' }}>
      {TRADEOFFS.map((tr) => (
        <li
          key={tr.n}
          style={{
            padding: '12px 0',
            borderBottom: '1px solid var(--line)',
            display: 'grid',
            gridTemplateColumns: '28px 1fr',
            gap: 12,
            fontSize: 13.5,
            lineHeight: 1.55,
          }}
        >
          <span className="mono" style={{ fontSize: 11, color: 'var(--ink-4)', paddingTop: 2 }}>
            {tr.n}
          </span>
          <span>
            <strong>{tr.title}</strong> {tr.body}
          </span>
        </li>
      ))}
    </ul>
  );
}

function NotesMap() {
  return (
    <div
      style={{
        border: '1px solid var(--line)',
        borderRadius: 'var(--radius-lg)',
        overflow: 'hidden',
        background: 'var(--panel)',
      }}
    >
      <table className="t-data" style={{ fontSize: 12.5 }}>
        <thead>
          <tr>
            <th style={{ width: 36 }}>#</th>
            <th>{t('Ghi chú', 'Note')}</th>
            <th style={{ width: 120 }}>{t('Màn hình', 'Screen')}</th>
            <th>{t('Anchor selector', 'Anchor selector')}</th>
            <th>{t('File nguồn', 'Source file')}</th>
          </tr>
        </thead>
        <tbody>
          {NOTE_MAP.map((row) => (
            <tr key={row.n}>
              <td className="mono" style={{ color: 'var(--ink-4)' }}>
                {row.n}
              </td>
              <td>{row.note}</td>
              <td style={{ color: 'var(--ink-2)' }}>{row.screen}</td>
              <td className="mono" style={{ fontSize: 11.5, color: 'var(--ink-2)' }}>
                {row.anchor}
              </td>
              <td className="mono" style={{ fontSize: 11.5, color: 'var(--ink-3)' }}>
                {row.source}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function PullQuote() {
  return (
    <div
      style={{
        background: 'var(--bg-soft)',
        border: '1px solid var(--line)',
        padding: '14px 16px',
        margin: '32px 0 28px',
        borderRadius: 'var(--radius-md)',
        display: 'flex',
        gap: 12,
        alignItems: 'flex-start',
      }}
    >
      <Compass
        size={16}
        strokeWidth={1.5}
        style={{ color: 'var(--accent-ink)', marginTop: 2 }}
        aria-hidden
      />
      <div>
        <div className="lbl" style={{ marginBottom: 6 }}>
          {t('Nếu bạn đọc tới đây', 'If you read this far')}
        </div>
        <p style={{ margin: 0, fontSize: 13.5, lineHeight: 1.6, color: 'var(--ink)' }}>
          {t('Phản hồi tôi muốn nhất là về', 'The feedback I most want is on the')}{' '}
          <strong>{t('màn hình Compliance', 'Compliance screen')}</strong>{' '}
          {t(
            '— cụ thể là liệu máy trạng thái vòng đời và bảng nhà cung cấp phụ có đọc ra đáng-tin-với-kiểm-toán hay là người-thiết-kế-giả-vờ-hiểu-compliance.',
            '— specifically whether the lifecycle state machine and the sub-processor table read as auditor-credible or as designer-pretending-to-know-compliance.',
          )}
        </p>
      </div>
    </div>
  );
}

function Footer() {
  return (
    <div
      style={{
        marginTop: 64,
        paddingTop: 24,
        borderTop: '1px solid var(--line)',
        display: 'flex',
        flexWrap: 'wrap',
        gap: 24,
        alignItems: 'center',
        fontSize: 12,
        color: 'var(--ink-3)',
      }}
    >
      <span style={{ flex: 1, minWidth: 240 }}>
        {t(
          'Thiết kế và prototype bằng React với một lớp design-token CSS riêng · 10/2025 – 01/2026',
          'Designed and prototyped in React with a custom design-token CSS layer · Oct 2025 – Jan 2026',
        )}
      </span>
      <Link
        to="/login"
        className="btn primary sm"
        style={{ textDecoration: 'none' }}
        data-testid="about-footer-cta"
      >
        {t('Đăng nhập', 'Sign in')} <ArrowRight size={12} strokeWidth={1.5} aria-hidden />
      </Link>
    </div>
  );
}
