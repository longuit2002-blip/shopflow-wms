/**
 * GuidedTour — a self-contained 10-step spotlight overlay that walks a
 * reviewer through the 10 design notes from the design handoff.
 *
 * Ported from the design canon `tour.jsx` (`TourProvider` + `useTour` +
 * `CoachMark`/`ReviewerPin`). This port deliberately keeps ONLY the
 * "reviewer-facing 10-note walkthrough" — a single, ordered spotlight tour
 * driven by a floating trigger. The canon's heavier surfaces are OMITTED:
 *   - the auto-show WelcomeOverlay (session-gated splash),
 *   - the persona-aware per-screen CoachMarks,
 *   - the full "Reviewer mode" with inline comment-pinning + a docked panel
 *     + the "Top 3 for a hurried reviewer" queue + reset/seen tracking.
 * Those are product-tour scope; the brief asks for the 10-note guided
 * walkthrough, which is what this component is.
 *
 * How steps resolve anchors: each step carries a CSS selector that targets a
 * `data-review="…"` / `data-tour="…"` attribute already shipped across the
 * screens + TopBar (the QA + tour contract from the handoff README). At
 * runtime `useAnchorRect` polls `document.querySelector(selector)` and
 * measures its bounding rect; the spotlight ring + copy card position
 * against that rect. If the anchored element is not on the current screen
 * (e.g. the reviewer is on Dashboard but the step targets the Compliance
 * residency card), the step renders its copy in a centered fallback card
 * that names the screen the note lives on — it never crashes.
 *
 * Conventions: reuses the design-system token vars (--ink, --accent,
 * --panel, --line, --shadow-pop, --radius-lg, --bg-soft, --font-mono,
 * --z-tour) + classes (.btn, .pill, .mono, .lbl, .kbd) + the lucide-react
 * icon set + the `t()`/`useLocale()` bilingual translator.
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import type { ForwardedRef, KeyboardEvent as ReactKeyboardEvent, ReactNode } from 'react';
import { Compass, X, ChevronLeft, ChevronRight } from 'lucide-react';
import { t, useLocale } from '../../hooks/useLocale';

// ── Step model ───────────────────────────────────────────────────────────────

interface BiText {
  vi: string;
  en: string;
}

interface TourStep {
  /** 1-based note id (matches the handoff "10 design notes" table). */
  id: number;
  /** CSS selector for the anchored DOM node already on the shipped screens. */
  anchor: string;
  /** Human screen name where the note lives (bilingual). */
  screen: BiText;
  /** Route path the anchor lives on (for the off-screen fallback hint). */
  screenPath: string;
  title: BiText;
  body: BiText;
}

/**
 * The 10 notes, in the canonical order from the handoff README table.
 * Titles + bodies are the richer Vietnamese copy from `tour.jsx`'s
 * REVIEWER_NOTES, with English translations alongside. Anchors match the
 * `data-review`/`data-tour` attributes verified in the shipped routes +
 * TopBar.
 */
const STEPS: readonly TourStep[] = [
  {
    id: 1,
    anchor: '[data-review="ochre"]',
    screen: { vi: 'Tồn kho', en: 'Inventory' },
    screenPath: '/inventory',
    title: { vi: 'Amber-ochre, không phải SaaS blue', en: 'Amber-ochre, not SaaS blue' },
    body: {
      vi: 'Tôi từ chối blue mặc định. Ba lý do: (1) blue đã thành tín hiệu generic — reviewer scan 5 portfolio đầu thấy 4 cái blue. (2) ShopFlow đọc warm hơn — kho hàng, không phải fintech. (3) Blue conflict với màu kênh Lazada. Amber-ochre #C9620E không conflict với bất kỳ kênh nào.',
      en: 'I rejected default blue. Three reasons: (1) blue has become a generic signal — a reviewer scanning 5 portfolios sees 4 of them blue. (2) ShopFlow reads warmer — a warehouse, not a fintech. (3) Blue conflicts with the Lazada channel colour. Amber-ochre #C9620E conflicts with no channel.',
    },
  },
  {
    id: 2,
    anchor: '[data-tour="tenant-pill"]',
    screen: { vi: 'Dashboard / Thanh trên', en: 'Dashboard / TopBar' },
    screenPath: '/dashboard',
    title: { vi: 'Nhận diện tenant trên mọi màn', en: 'Tenant identity on every screen' },
    body: {
      vi: 'Multi-tenancy thường bị giấu — nó là chi tiết hạ tầng. ShopFlow ngược lại: tenant context hiện trên top mọi màn (tên + ERC + region + tên DB). Khi auditor walk-through, họ không bao giờ phải hỏi "tôi đang xem dữ liệu của ai".',
      en: 'Multi-tenancy is usually hidden — treated as infra trivia. ShopFlow does the opposite: tenant context sits at the top of every screen (name + ERC + region + DB name). When an auditor walks through, they never have to ask "whose data am I looking at".',
    },
  },
  {
    id: 3,
    anchor: '[data-review="residency"]',
    screen: { vi: 'Tuân thủ', en: 'Compliance' },
    screenPath: '/compliance',
    title: { vi: 'Phơi bày vùng CSDL từng tenant', en: 'Per-tenant DB residency surfaced' },
    body: {
      vi: 'Cô lập dữ liệu thường nằm trong slide bán hàng. Đây nó là một card thật: vùng chính, vùng sao lưu, ràng buộc egress, và tên DB vật lý riêng cho tenant này. Reviewer thấy ngay đây là quyết định kiến trúc, không phải khẩu hiệu.',
      en: 'Data isolation usually lives in a sales slide. Here it is a real card: primary region, backup region, egress constraints, and the physical DB name dedicated to this tenant. A reviewer sees immediately this is an architectural decision, not a slogan.',
    },
  },
  {
    id: 4,
    anchor: '[data-review="saga"]',
    screen: { vi: 'Đơn hàng', en: 'Orders' },
    screenPath: '/orders',
    title: { vi: 'Saga ledger như xương sống forensic', en: 'Saga ledger as forensic spine' },
    body: {
      vi: 'Mỗi chuyển trạng thái của đơn là một dòng trong saga ledger — kèm trace ID, idempotency key, retry count. Bạn có thể replay hoặc refund từ bất kỳ bước nào. Đây là centerpiece: timeline forensic per-order, không phải status badge.',
      en: 'Every order state transition is a row in the saga ledger — with trace ID, idempotency key, retry count. You can replay or refund from any step. This is the centerpiece: a per-order forensic timeline, not a status badge.',
    },
  },
  {
    id: 5,
    anchor: '[data-review="idem"]',
    screen: { vi: 'Nhật ký kiểm toán', en: 'Audit' },
    screenPath: '/audit',
    title: { vi: 'Idempotency key như cột', en: 'Idempotency keys as columns' },
    body: {
      vi: '99% audit UI không bao giờ surface idempotency key dù backend có. Tôi để nó visible với tooltip giải thích. Senior engineer reviewer thấy ngay đây là người hiểu saga + outbox pattern, không chỉ design surface. Là một engineering literacy tell.',
      en: '99% of audit UIs never surface the idempotency key even though the backend has it. I keep it visible with an explaining tooltip. A senior-engineer reviewer sees at once this is someone who understands the saga + outbox pattern, not just surface design. It is an engineering-literacy tell.',
    },
  },
  {
    id: 6,
    anchor: '[data-review="vn-content"]',
    screen: { vi: 'Tồn kho', en: 'Inventory' },
    screenPath: '/inventory',
    title: { vi: 'Tiếng Việt thật, không phải dịch máy', en: 'Vietnamese content, not translated' },
    body: {
      vi: 'Categories là Yến sào / Cà phê / Đặc sản / Mật ong — không phải "apparel / beverages". Tên SKU tiếng Việt thật, không "Sample Product 1". Tín hiệu reviewer nhận ra designer hiểu thực tế catalog của SME Việt Nam.',
      en: 'Categories are Yến sào / Cà phê / Đặc sản / Mật ong — not "apparel / beverages". SKU names are real Vietnamese, not "Sample Product 1". A signal that the designer understands the catalog reality of a Vietnamese SME.',
    },
  },
  {
    id: 7,
    anchor: '[data-review="border-card"]',
    screen: { vi: 'Dashboard', en: 'Dashboard' },
    screenPath: '/dashboard',
    title: { vi: 'Đường viền, không phải bóng đổ', en: 'Borders, not shadows' },
    body: {
      vi: 'Hầu hết dashboard B2B dùng shadow-md cho mọi card. Tôi dùng border 1px; shadow chỉ dành riêng cho floating element (modal, drawer, popover). Triết lý: sharp data, soft chrome. Tham chiếu: Bloomberg + Linear.',
      en: 'Most B2B dashboards use shadow-md on every card. I use a 1px border; shadow is reserved only for floating elements (modal, drawer, popover). Philosophy: sharp data, soft chrome. Reference: Bloomberg + Linear.',
    },
  },
  {
    id: 8,
    anchor: '[data-tour="live-indicator"]',
    screen: { vi: 'Dashboard / Thanh trên', en: 'Dashboard / TopBar' },
    screenPath: '/dashboard',
    title: {
      vi: 'Dưới 1024px hiện thông báo, không responsive',
      en: '< 1024px shows a notice, no responsive',
    },
    body: {
      vi: 'WMS không make operational sense trên điện thoại. Cố responsive xuống mobile = hỏng nguyên tắc mật độ + giả vờ universal. Kỷ luật thiết kế trung thực: dưới 1024px hiện thông báo "dùng màn hình lớn hơn", không pretend. Chấm xanh nhấp nháy = WebSocket đang kết nối.',
      en: 'A WMS makes no operational sense on a phone. Forcing responsive down to mobile breaks the density principle and pretends to be universal. Honest design discipline: under 1024px show a "use a larger screen" notice, do not pretend. The pulsing green dot = the WebSocket is connected.',
    },
  },
  {
    id: 9,
    anchor: '[data-review="subprocessors"]',
    screen: { vi: 'Tuân thủ', en: 'Compliance' },
    screenPath: '/compliance',
    title: {
      vi: 'Nhà cung cấp phụ kèm vùng + ngày DPA',
      en: 'Sub-processors with region + DPA date',
    },
    body: {
      vi: 'Generic AI viết "Database provider, Cloud-based". Đây ghi rõ AWS RDS Postgres 16 ap-southeast-1 với ngày cập nhật DPA thật cho từng dòng. Reviewer 5 giây nhận ra người này hiểu prod ops, không chỉ design surface.',
      en: 'Generic AI writes "Database provider, Cloud-based". Here it spells out AWS RDS Postgres 16 ap-southeast-1 with a real last-DPA-update date per row. In 5 seconds a reviewer recognises this is someone who understands prod ops, not just surface design.',
    },
  },
  {
    id: 10,
    anchor: '[data-review="empty"]',
    screen: { vi: 'Tồn kho · Đơn hàng', en: 'Inventory · Orders' },
    screenPath: '/inventory',
    title: {
      vi: 'Empty state là thiết kế, không phải trống',
      en: 'Empty state as deliberate design',
    },
    body: {
      vi: 'Empty state có 3 phần: (a) custom SVG line-art theo chủ đề, (b) lý do empty bằng tiếng Việt cụ thể, (c) một hành động chính. Generic AI hiển thị "No data" — tôi treat empty state là khoảnh khắc dạy người dùng làm gì tiếp theo.',
      en: 'An empty state has 3 parts: (a) themed custom SVG line-art, (b) the specific reason it is empty in Vietnamese, (c) one primary action. Generic AI shows "No data" — I treat the empty state as a moment to teach the user what to do next.',
    },
  },
] as const;

const TOTAL = STEPS.length;

// z-index layering, all relative to the kernel `--z-tour` (100) token. CSS
// `calc()` strings are passed straight through to inline style (React keeps
// them verbatim for custom/`zIndex` numeric-or-string values).
const Z = {
  trigger: 'calc(var(--z-tour) - 1)',
  backdrop: 'var(--z-tour)',
  ring: 'calc(var(--z-tour) + 1)',
  pin: 'calc(var(--z-tour) + 2)',
  card: 'calc(var(--z-tour) + 3)',
} as const;

// ── Anchor measurement ───────────────────────────────────────────────────────

interface AnchorRect {
  top: number;
  left: number;
  width: number;
  height: number;
  bottom: number;
  right: number;
}

/**
 * Polls `document.querySelector(selector)` and tracks its bounding rect.
 * Returns null while the anchor is absent (off-screen / not yet mounted).
 * Re-measures on an interval + on resize/scroll so the spotlight follows the
 * element if the layout shifts. Cleans up every listener + the interval on
 * unmount or selector change.
 */
function useAnchorRect(selector: string | null): AnchorRect | null {
  const [rect, setRect] = useState<AnchorRect | null>(null);

  useEffect(() => {
    if (!selector) {
      setRect(null);
      return;
    }
    let alive = true;
    const measure = () => {
      if (!alive) return;
      const el = document.querySelector(selector);
      if (!el) {
        setRect(null);
        return;
      }
      const r = el.getBoundingClientRect();
      setRect({
        top: r.top,
        left: r.left,
        width: r.width,
        height: r.height,
        bottom: r.bottom,
        right: r.right,
      });
    };
    measure();
    const id = window.setInterval(measure, 250);
    window.addEventListener('resize', measure);
    window.addEventListener('scroll', measure, true);
    return () => {
      alive = false;
      window.clearInterval(id);
      window.removeEventListener('resize', measure);
      window.removeEventListener('scroll', measure, true);
    };
  }, [selector]);

  return rect;
}

interface CardPos {
  top: number;
  left: number;
}

const CARD_W = 340;
const CARD_H = 220;

/** Position the copy card next to the anchor rect, preferring right → left → below → above. */
function cardPos(rect: AnchorRect): CardPos {
  const margin = 16;
  const vw = window.innerWidth;
  const vh = window.innerHeight;
  const clampTop = (top: number) => Math.max(margin, Math.min(top, vh - CARD_H - margin));
  const clampLeft = (left: number) => Math.max(margin, Math.min(left, vw - CARD_W - margin));

  if (rect.right + CARD_W + margin < vw) {
    return { top: clampTop(rect.top), left: rect.right + margin };
  }
  if (rect.left - CARD_W - margin > 0) {
    return { top: clampTop(rect.top), left: rect.left - CARD_W - margin };
  }
  if (rect.bottom + CARD_H + margin < vh) {
    return { top: rect.bottom + margin, left: clampLeft(rect.left) };
  }
  return { top: clampTop(rect.top - CARD_H - margin), left: clampLeft(rect.left) };
}

// ── Component ────────────────────────────────────────────────────────────────

export function GuidedTour() {
  // Re-render bilingual copy on locale flip.
  useLocale();

  const [open, setOpen] = useState(false);
  const [idx, setIdx] = useState(0);

  // `idx` is always clamped to [0, TOTAL-1] by next/back, so the indexed read
  // is provably in range — the non-null assertion documents that invariant
  // for noUncheckedIndexedAccess. The extra clamp is belt-and-braces.
  const step = STEPS[Math.min(Math.max(idx, 0), TOTAL - 1)]!;
  const rect = useAnchorRect(open ? step.anchor : null);

  const cardRef = useRef<HTMLDivElement | null>(null);
  const triggerRef = useRef<HTMLButtonElement | null>(null);

  const start = useCallback(() => {
    setIdx(0);
    setOpen(true);
  }, []);

  const close = useCallback(() => {
    setOpen(false);
    // Return focus to the trigger so keyboard users are not stranded.
    triggerRef.current?.focus();
  }, []);

  const next = useCallback(() => {
    setIdx((i) => {
      if (i + 1 >= TOTAL) {
        setOpen(false);
        return 0;
      }
      return i + 1;
    });
  }, []);

  const back = useCallback(() => {
    setIdx((i) => Math.max(0, i - 1));
  }, []);

  const isFirst = idx === 0;
  const isLast = idx === TOTAL - 1;

  // Keyboard: ←/→ to move, Esc to close. Ignore arrows while typing.
  useEffect(() => {
    if (!open) return;
    const onKey = (e: KeyboardEvent) => {
      const target = e.target as HTMLElement | null;
      const tag = target?.tagName;
      const typing = tag === 'INPUT' || tag === 'TEXTAREA' || target?.isContentEditable === true;
      if (e.key === 'Escape') {
        e.preventDefault();
        close();
      } else if (!typing && e.key === 'ArrowRight') {
        e.preventDefault();
        next();
      } else if (!typing && e.key === 'ArrowLeft') {
        e.preventDefault();
        back();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [open, close, next, back]);

  // Move focus into the card whenever it opens / the step changes, so the
  // tour is keyboard-reachable and the focus-trap below has somewhere to land.
  useEffect(() => {
    if (open) cardRef.current?.focus();
  }, [open, idx]);

  // Minimal focus trap: keep Tab cycling within the card while the tour is open.
  const onCardKeyDown = useCallback((e: ReactKeyboardEvent<HTMLDivElement>) => {
    if (e.key !== 'Tab') return;
    const root = cardRef.current;
    if (!root) return;
    const focusables = root.querySelectorAll<HTMLElement>(
      'button, [href], [tabindex]:not([tabindex="-1"])',
    );
    if (focusables.length === 0) return;
    const first = focusables[0]!;
    const last = focusables[focusables.length - 1]!;
    const active = document.activeElement;
    if (e.shiftKey && active === first) {
      e.preventDefault();
      last.focus();
    } else if (!e.shiftKey && active === last) {
      e.preventDefault();
      first.focus();
    }
  }, []);

  return (
    <>
      <TourTrigger ref={triggerRef} open={open} onOpen={start} />
      {open && (
        <div
          // Dim backdrop. Clicking it closes the tour (matches canon).
          aria-hidden
          onClick={close}
          style={{
            position: 'fixed',
            inset: 0,
            zIndex: Z.backdrop,
            background: 'rgba(26, 26, 24, 0.45)',
            animation: 'fadeIn 180ms var(--ease-out, ease-out)',
          }}
        />
      )}
      {open && rect && <SpotlightRing rect={rect} id={step.id} />}
      {open && (
        <TourCard
          ref={cardRef}
          step={step}
          idx={idx}
          rect={rect}
          isFirst={isFirst}
          isLast={isLast}
          onNext={next}
          onBack={back}
          onClose={close}
          onKeyDown={onCardKeyDown}
        />
      )}
    </>
  );
}

// ── Trigger button ───────────────────────────────────────────────────────────

interface TourTriggerProps {
  open: boolean;
  onOpen: () => void;
  ref?: ForwardedRef<HTMLButtonElement>;
}

/** React 19 passes `ref` as a normal prop for function components. */
function TourTrigger({ open, onOpen, ref }: TourTriggerProps) {
  const label = t('Hướng dẫn', 'Guided tour');
  return (
    <button
      ref={ref}
      type="button"
      className="btn"
      onClick={onOpen}
      aria-label={t('Mở hướng dẫn có chú thích', 'Open the guided tour')}
      aria-haspopup="dialog"
      aria-expanded={open}
      title={label}
      style={{
        position: 'fixed',
        right: 16,
        bottom: 16,
        // One below the overlay so it never overpaints the spotlight, but
        // above page content.
        zIndex: Z.trigger,
        height: 36,
        paddingInline: 14,
        gap: 8,
        background: 'var(--accent-soft)',
        color: 'var(--accent-ink)',
        borderColor: 'var(--accent-line)',
        boxShadow: 'var(--shadow-pop)',
        fontWeight: 600,
      }}
    >
      <Compass size={15} strokeWidth={1.75} aria-hidden />
      <span className="nb">{label}</span>
    </button>
  );
}

// ── Spotlight ring ───────────────────────────────────────────────────────────

function SpotlightRing({ rect, id }: { rect: AnchorRect; id: number }) {
  return (
    <>
      <div
        aria-hidden
        style={{
          position: 'fixed',
          pointerEvents: 'none',
          top: rect.top - 5,
          left: rect.left - 5,
          width: rect.width + 10,
          height: rect.height + 10,
          border: '2px solid var(--accent)',
          borderRadius: 'var(--radius-md)',
          boxShadow: '0 0 0 6px var(--accent-soft)',
          zIndex: Z.ring,
          transition: 'all 200ms var(--ease-out, ease-out)',
        }}
      />
      <div
        aria-hidden
        style={{
          position: 'fixed',
          top: rect.top - 12,
          left: rect.left + rect.width - 12,
          width: 24,
          height: 24,
          borderRadius: 12,
          background: 'var(--accent)',
          color: 'var(--ink-inv)',
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'center',
          fontSize: 11,
          fontWeight: 700,
          fontFamily: 'var(--font-mono)',
          zIndex: Z.pin,
          boxShadow: '0 1px 4px rgba(0, 0, 0, 0.2)',
          animation: 'pinPulse 1.6s var(--ease-in-out, ease-in-out) infinite',
        }}
      >
        {id}
      </div>
    </>
  );
}

// ── Copy card ────────────────────────────────────────────────────────────────

interface TourCardProps {
  step: TourStep;
  idx: number;
  rect: AnchorRect | null;
  isFirst: boolean;
  isLast: boolean;
  onNext: () => void;
  onBack: () => void;
  onClose: () => void;
  onKeyDown: (e: ReactKeyboardEvent<HTMLDivElement>) => void;
  ref?: ForwardedRef<HTMLDivElement>;
}

function TourCard({
  step,
  idx,
  rect,
  isFirst,
  isLast,
  onNext,
  onBack,
  onClose,
  onKeyDown,
  ref,
}: TourCardProps) {
  const pos: CardPos = rect
    ? cardPos(rect)
    : // Off-screen anchor → centered fallback.
      {
        top: Math.max(16, window.innerHeight / 2 - CARD_H / 2),
        left: Math.max(16, window.innerWidth / 2 - CARD_W / 2),
      };

  const title = t(step.title.vi, step.title.en);
  const screenName = t(step.screen.vi, step.screen.en);

  return (
    <div
      ref={ref}
      role="dialog"
      aria-modal="true"
      aria-label={`${t('Bước', 'Step')} ${idx + 1} / ${TOTAL} — ${title}`}
      tabIndex={-1}
      onClick={(e) => e.stopPropagation()}
      onKeyDown={onKeyDown}
      style={{
        position: 'fixed',
        top: pos.top,
        left: pos.left,
        width: CARD_W,
        maxWidth: 'calc(100vw - 32px)',
        background: 'var(--panel)',
        border: '1px solid var(--line)',
        borderLeft: '4px solid var(--accent)',
        borderRadius: 'var(--radius-lg)',
        boxShadow: 'var(--shadow-pop)',
        padding: 16,
        zIndex: Z.card,
        animation: 'popIn 200ms var(--ease-out, ease-out)',
        outline: 'none',
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: 8, marginBottom: 8 }}>
        <span
          className="pill accent"
          style={{ fontFamily: 'var(--font-mono)' }}
        >{`${t('Ghi chú', 'Note')} ${step.id} / ${TOTAL}`}</span>
        <span style={{ flex: 1 }} />
        <button
          type="button"
          className="btn ghost sm"
          onClick={onClose}
          aria-label={t('Đóng hướng dẫn', 'Close tour')}
          style={{ paddingInline: 6 }}
        >
          <X size={14} aria-hidden />
        </button>
      </div>

      <div
        style={{
          fontSize: 14,
          fontWeight: 600,
          color: 'var(--ink)',
          lineHeight: 1.35,
          marginBottom: 6,
        }}
      >
        {title}
      </div>

      {!rect && (
        <div
          style={{
            fontSize: 11.5,
            color: 'var(--ink-3)',
            marginBottom: 8,
            padding: '6px 8px',
            background: 'var(--bg-soft)',
            border: '1px solid var(--line)',
            borderRadius: 'var(--radius-sm)',
          }}
        >
          {t('Phần tử này sống trên màn ', 'This element lives on the ')}
          <b>{screenName}</b>
          {t(' — mở màn đó để xem điểm được tô sáng.', ' screen — open it to see the highlight.')}
          <span className="mono" style={{ display: 'block', marginTop: 2, color: 'var(--ink-4)' }}>
            {step.screenPath}
          </span>
        </div>
      )}

      <div style={{ fontSize: 12.5, color: 'var(--ink-2)', lineHeight: 1.6 }}>
        {t(step.body.vi, step.body.en)}
      </div>

      <div
        style={{
          display: 'flex',
          alignItems: 'center',
          justifyContent: 'space-between',
          marginTop: 14,
        }}
      >
        <span className="lbl" style={{ fontFamily: 'var(--font-mono)' }}>
          {idx + 1} / {TOTAL}
        </span>
        <div style={{ display: 'flex', gap: 6 }}>
          <button
            type="button"
            className="btn sm"
            onClick={onBack}
            disabled={isFirst}
            aria-label={t('Bước trước', 'Previous step')}
          >
            <ChevronLeft size={13} aria-hidden /> {t('Trước', 'Prev')}
          </button>
          <button
            type="button"
            className="btn primary sm"
            onClick={onNext}
            aria-label={
              isLast ? t('Hoàn tất hướng dẫn', 'Finish tour') : t('Bước tiếp', 'Next step')
            }
          >
            {isLast ? t('Hoàn tất', 'Done') : t('Tiếp', 'Next')}
            {!isLast && <ChevronRight size={13} aria-hidden />}
          </button>
        </div>
      </div>

      <div
        style={{
          marginTop: 10,
          paddingTop: 10,
          borderTop: '1px dashed var(--line)',
          display: 'flex',
          gap: 12,
          fontSize: 10.5,
          color: 'var(--ink-3)',
        }}
      >
        <KbdHint>
          <kbd className="kbd">←</kbd> <kbd className="kbd">→</kbd> {t('điều hướng', 'navigate')}
        </KbdHint>
        <KbdHint>
          <kbd className="kbd">Esc</kbd> {t('đóng', 'close')}
        </KbdHint>
      </div>
    </div>
  );
}

function KbdHint({ children }: { children: ReactNode }) {
  return <span style={{ display: 'inline-flex', alignItems: 'center', gap: 4 }}>{children}</span>;
}
