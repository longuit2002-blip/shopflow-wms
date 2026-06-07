/**
 * Pick — the Warehouse Operator's mobile-first picking screen.
 *
 * Ported from the design handoff `screen-orders.jsx` OPERATOR branch
 * (`OperatorPickWave` + `CannotFindModal`). The design app renders this
 * when `role === 'operator'`; here it ships as a dedicated, perm-gated
 * route at `/_auth/pick` (no role switcher).
 *
 * Faithful to the handoff but re-laid-out for a phone/tablet held in the
 * aisle: a narrow centred column (max ~520px) even on desktop, big tap
 * targets, one pick item at a time emphasised (the `active` row), zone
 * grouping, bin code + quantity surfaced large, and a scan field. The
 * dark wave header carries the wave id + live progress.
 *
 * Data is mocked in the frontend (no pick-wave backend endpoints exist on
 * this route yet — Sprint-13.5 wires the Picker/Packer surfaces to the
 * real saga). SKU names + bin codes mirror the other screens (Yến sào /
 * Cà phê / đặc sản; bins like B-04-12).
 *
 * State is local React (`useState`): per-item picked flag, which item is
 * `active`, the scan input value, and the open "cannot find" modal.
 *
 * `data-review="operator-pick"` anchor preserved from the handoff (QA +
 * guided-tour contract).
 */

import { Fragment, useMemo, useState } from 'react';
import { createFileRoute } from '@tanstack/react-router';
import { Waypoints, ScanLine, MapPin, Check, AlertTriangle, PackageCheck, X } from 'lucide-react';
import { Pill } from '../../components/primitives/Pill';
import { t, useLocale } from '../../hooks/useLocale';

// ── Pick-task shape + mock wave ─────────────────────────────────────────────

interface PickItem {
  sku: string;
  name: string;
  bin: string;
  qty: number;
  order: string;
  done: boolean;
}

interface PickZone {
  zone: string;
  label: { vi: string; en: string };
  items: PickItem[];
}

interface PickWave {
  id: string;
  operator: string;
  zones: PickZone[];
}

const PICK_WAVE: PickWave = {
  id: 'PW-2026-05-11-007',
  operator: 'Nguyễn Văn An',
  zones: [
    {
      zone: 'A-02',
      label: { vi: 'Yến · tủ mát', en: 'Bird-nest · cold cabinet' },
      items: [
        {
          sku: 'YS-TINH-CHE-100G',
          name: 'Yến sào tinh chế 100g',
          bin: 'A-02-12',
          qty: 2,
          order: 'SO-2026-05-0042',
          done: true,
        },
        {
          sku: 'YS-NGUYEN-TO-50G',
          name: 'Yến sào nguyên tổ 50g',
          bin: 'A-02-14',
          qty: 1,
          order: 'SO-2026-05-0039',
          done: true,
        },
        {
          sku: 'YS-CHUNG-DUONG-6',
          name: 'Yến chưng đường phèn 6 hũ',
          bin: 'A-01-03',
          qty: 1,
          order: 'SO-2026-05-0036',
          done: false,
        },
      ],
    },
    {
      zone: 'B-02',
      label: { vi: 'Đặc sản · kệ thường', en: 'Specialty · ambient' },
      items: [
        {
          sku: 'KB-MIENG-LON-200G',
          name: 'Khô bò miếng lớn 200g',
          bin: 'B-02-07',
          qty: 2,
          order: 'SO-2026-05-0036',
          done: false,
        },
        {
          sku: 'NH-MAT-ONG-500G',
          name: 'Mật ong rừng 500g',
          bin: 'B-01-05',
          qty: 1,
          order: 'SO-2026-05-0042',
          done: false,
        },
      ],
    },
    {
      zone: 'B-03',
      label: { vi: 'Cà phê · kệ thường', en: 'Coffee · ambient' },
      items: [
        {
          sku: 'CF-RANG-XAY-500G',
          name: 'Cà phê rang xay 500g · Robusta',
          bin: 'B-03-11',
          qty: 3,
          order: 'SO-2026-05-0042',
          done: false,
        },
        {
          sku: 'CF-HAT-NGUYEN-1KG',
          name: 'Cà phê hạt nguyên 1kg · Arabica',
          bin: 'B-03-12',
          qty: 1,
          order: 'SO-2026-05-0038',
          done: false,
        },
      ],
    },
  ],
};

// Reason choices for the "cannot find" compensation modal.
const CANNOT_FIND_REASONS: { vi: string; en: string }[] = [
  { vi: 'Bin trống', en: 'Bin empty' },
  { vi: 'Sai vị trí', en: 'Wrong location' },
  { vi: 'Hư hại', en: 'Damaged' },
  { vi: 'Lý do khác', en: 'Other reason' },
];

// ── Route ───────────────────────────────────────────────────────────────────

export const Route = createFileRoute('/_auth/pick')({
  component: PickRouteComponent,
});

function PickRouteComponent() {
  useLocale();

  // Flatten the wave into a single ordered list so we can address "the
  // current pick item" by index — the mobile flow walks items one at a
  // time. Each entry keeps its zone for the grouped render.
  const initialItems = useMemo<PickItem[]>(
    () => PICK_WAVE.zones.flatMap((z) => z.items.map((i) => ({ ...i }))),
    [],
  );

  const [items, setItems] = useState<PickItem[]>(initialItems);
  const [scan, setScan] = useState('');
  // `reasonFor` holds the index (into `items`) of the item whose
  // "cannot find" modal is open, or null. Index-based so the modal reads
  // a guaranteed-in-range entry.
  const [reasonFor, setReasonFor] = useState<number | null>(null);

  const total = items.length;
  const done = items.filter((i) => i.done).length;
  const pct = total === 0 ? 0 : Math.round((done / total) * 100);

  // First not-yet-picked item is the "active" one (highlighted + the scan
  // field's implicit target). Clamp keeps it provably in range.
  const activeIdx = items.findIndex((i) => !i.done);
  const activeItem = activeIdx >= 0 ? items[activeIdx] : undefined;

  const togglePicked = (idx: number) => {
    setItems((prev) => prev.map((it, i) => (i === idx ? { ...it, done: !it.done } : it)));
  };

  // Scan submit: if the scanned code matches the active item's SKU or bin,
  // mark it picked. Otherwise leave state untouched (a real device would
  // beep). Mock-only — no backend.
  const submitScan = () => {
    const code = scan.trim().toUpperCase();
    if (code === '') return;
    if (activeIdx >= 0 && activeItem) {
      if (code === activeItem.sku.toUpperCase() || code === activeItem.bin.toUpperCase()) {
        togglePicked(activeIdx);
      }
    }
    setScan('');
  };

  const reasonItem = reasonFor !== null ? items[reasonFor] : undefined;

  // Group the (possibly mutated) flat list back into zones for the render,
  // preserving each item's index in the flat list for handlers.
  const grouped = useMemo(() => {
    const byZone = new Map<
      string,
      { zone: PickZone; entries: { item: PickItem; idx: number }[] }
    >();
    items.forEach((item, idx) => {
      const zoneDef = PICK_WAVE.zones.find((z) => z.items.some((zi) => zi.sku === item.sku));
      const key = zoneDef ? zoneDef.zone : '—';
      const bucket = byZone.get(key);
      if (bucket) {
        bucket.entries.push({ item, idx });
      } else if (zoneDef) {
        byZone.set(key, { zone: zoneDef, entries: [{ item, idx }] });
      }
    });
    return Array.from(byZone.values());
  }, [items]);

  return (
    <div
      className="touch scroll-y"
      data-review="operator-pick"
      data-tour="operator-pick"
      style={{ flex: 1, minHeight: 0, background: 'var(--bg-sunken)' }}
    >
      {/* Centred narrow column — mobile-first even on desktop */}
      <div
        style={{
          maxWidth: 520,
          margin: '0 auto',
          minHeight: '100%',
          background: 'var(--panel)',
          borderLeft: '1px solid var(--line)',
          borderRight: '1px solid var(--line)',
          display: 'flex',
          flexDirection: 'column',
        }}
      >
        <WaveHeader
          waveId={PICK_WAVE.id}
          operator={PICK_WAVE.operator}
          done={done}
          total={total}
          pct={pct}
        />

        <ScanBar
          value={scan}
          onChange={setScan}
          onSubmit={submitScan}
          activeName={activeItem?.name ?? null}
          activeBin={activeItem?.bin ?? null}
        />

        <div style={{ flex: 1 }}>
          {grouped.map(({ zone, entries }) => {
            const zoneDone = entries.filter((e) => e.item.done).length;
            return (
              <section key={zone.zone} aria-label={`${t('Khu', 'Zone')} ${zone.zone}`}>
                <div
                  className="row-zone"
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: 10,
                    padding: '12px 18px',
                    background: 'var(--bg-sunken)',
                    borderTop: '1px solid var(--line)',
                    borderBottom: '1px solid var(--line)',
                    fontSize: 14,
                    fontWeight: 600,
                  }}
                >
                  <MapPin size={15} strokeWidth={1.75} aria-hidden />
                  <span>
                    {t('Khu', 'Zone')} {zone.zone}
                  </span>
                  <span style={{ fontSize: 12, fontWeight: 500, color: 'var(--ink-3)' }}>
                    {t(zone.label.vi, zone.label.en)}
                  </span>
                  <span style={{ flex: 1 }} />
                  <span className="mono tnum" style={{ fontSize: 12, color: 'var(--ink-3)' }}>
                    {zoneDone}/{entries.length}
                  </span>
                </div>

                {entries.map(({ item, idx }) => (
                  <PickRow
                    key={item.sku}
                    item={item}
                    isActive={idx === activeIdx}
                    onToggle={() => togglePicked(idx)}
                    onCannotFind={() => setReasonFor(idx)}
                  />
                ))}
              </section>
            );
          })}

          <div style={{ padding: '22px 18px' }}>
            <button
              type="button"
              className="btn xl primary"
              style={{ width: '100%', justifyContent: 'center' }}
              disabled={done === 0}
            >
              <PackageCheck size={18} strokeWidth={1.75} aria-hidden />
              {t(
                `Hoàn thành đợt · chuyển ${done} mặt hàng sang đóng gói`,
                `Finish wave · move ${done} items to packing`,
              )}
            </button>
          </div>
        </div>
      </div>

      {reasonFor !== null && reasonItem && (
        <CannotFindModal item={reasonItem} onClose={() => setReasonFor(null)} />
      )}
    </div>
  );
}

// ── Wave header (dark, sticky) ──────────────────────────────────────────────

function WaveHeader({
  waveId,
  operator,
  done,
  total,
  pct,
}: {
  waveId: string;
  operator: string;
  done: number;
  total: number;
  pct: number;
}) {
  return (
    <div
      style={{
        position: 'sticky',
        top: 0,
        zIndex: 5,
        background: 'var(--ink)',
        color: 'var(--ink-inv)',
        padding: '16px 18px',
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: 12 }}>
        <Waypoints size={20} strokeWidth={1.75} aria-hidden />
        <div style={{ flex: 1, minWidth: 0 }}>
          <div
            style={{
              fontSize: 10.5,
              opacity: 0.7,
              letterSpacing: '0.06em',
              textTransform: 'uppercase',
            }}
          >
            {t('Đợt nhặt hàng', 'Pick wave')}
          </div>
          <div className="mono" style={{ fontSize: 17, fontWeight: 600 }}>
            {waveId}
          </div>
          <div style={{ fontSize: 11, opacity: 0.65 }}>{operator}</div>
        </div>
        <div style={{ textAlign: 'right', flex: 'none' }}>
          <div className="mono tnum" style={{ fontSize: 30, fontWeight: 600, lineHeight: 1.05 }}>
            {done}/{total}
          </div>
          <div style={{ fontSize: 11, opacity: 0.7 }}>
            {t(`${pct}% hoàn tất`, `${pct}% complete`)}
          </div>
        </div>
      </div>
      {/* Progress bar */}
      <div
        role="progressbar"
        aria-valuenow={pct}
        aria-valuemin={0}
        aria-valuemax={100}
        aria-label={t('Tiến độ nhặt hàng', 'Picking progress')}
        style={{
          marginTop: 12,
          height: 6,
          borderRadius: 3,
          background: 'rgba(255,255,255,0.18)',
          overflow: 'hidden',
        }}
      >
        <div
          style={{
            width: `${pct}%`,
            height: '100%',
            background: 'var(--ok)',
            transition: 'width 160ms ease',
          }}
        />
      </div>
    </div>
  );
}

// ── Scan bar ────────────────────────────────────────────────────────────────

function ScanBar({
  value,
  onChange,
  onSubmit,
  activeName,
  activeBin,
}: {
  value: string;
  onChange: (v: string) => void;
  onSubmit: () => void;
  activeName: string | null;
  activeBin: string | null;
}) {
  return (
    <form
      onSubmit={(e) => {
        e.preventDefault();
        onSubmit();
      }}
      style={{
        padding: '14px 18px',
        borderBottom: '1px solid var(--line)',
        background: 'var(--accent-soft)',
      }}
    >
      <label htmlFor="pick-scan" className="lbl" style={{ display: 'block', marginBottom: 6 }}>
        {activeName
          ? t(`Quét mặt hàng tiếp theo · ${activeName}`, `Scan next item · ${activeName}`)
          : t('Quét mã SKU hoặc vị trí', 'Scan SKU or bin code')}
      </label>
      <div style={{ display: 'flex', gap: 8 }}>
        <input
          id="pick-scan"
          value={value}
          onChange={(e) => onChange(e.target.value)}
          placeholder={activeBin ?? t('VD: YS-TINH-CHE-100G', 'e.g. YS-TINH-CHE-100G')}
          aria-label={t('Mã SKU hoặc vị trí', 'SKU or bin code')}
          autoComplete="off"
          autoCapitalize="characters"
          style={{
            flex: 1,
            minWidth: 0,
            fontFamily: 'var(--font-mono)',
            fontSize: 16,
            padding: '0 12px',
            height: 48,
          }}
        />
        <button
          type="submit"
          className="btn xl primary"
          style={{ flex: 'none', paddingLeft: 18, paddingRight: 18 }}
          aria-label={t('Quét', 'Scan')}
        >
          <ScanLine size={18} strokeWidth={1.75} aria-hidden />
          {t('Quét', 'Scan')}
        </button>
      </div>
    </form>
  );
}

// ── Pick row (one item — big tap target) ────────────────────────────────────

function PickRow({
  item,
  isActive,
  onToggle,
  onCannotFind,
}: {
  item: PickItem;
  isActive: boolean;
  onToggle: () => void;
  onCannotFind: () => void;
}) {
  return (
    <div
      data-review={isActive ? 'pick-active' : undefined}
      style={{
        display: 'flex',
        flexDirection: 'column',
        gap: 12,
        padding: '16px 18px',
        borderBottom: '1px solid var(--line)',
        background: isActive ? 'var(--accent-soft)' : item.done ? 'var(--bg-soft)' : 'transparent',
        opacity: item.done ? 0.62 : 1,
      }}
    >
      <div style={{ display: 'flex', alignItems: 'center', gap: 14 }}>
        <button
          type="button"
          onClick={onToggle}
          aria-pressed={item.done}
          aria-label={
            item.done
              ? t(`Bỏ đánh dấu đã nhặt · ${item.name}`, `Unmark picked · ${item.name}`)
              : t(`Đánh dấu đã nhặt · ${item.name}`, `Mark picked · ${item.name}`)
          }
          style={{
            flex: 'none',
            width: 52,
            height: 52,
            borderRadius: 8,
            border: `2px solid ${item.done ? 'var(--ok)' : 'var(--line-strong)'}`,
            background: item.done ? 'var(--ok)' : 'var(--panel)',
            cursor: 'pointer',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
          }}
        >
          {item.done && <Check size={28} strokeWidth={3} style={{ color: '#fff' }} aria-hidden />}
        </button>
        <div style={{ minWidth: 0, flex: 1 }}>
          <div
            style={{
              fontSize: 16,
              fontWeight: 600,
              lineHeight: 1.25,
              textDecoration: item.done ? 'line-through' : 'none',
            }}
          >
            {item.name}
          </div>
          <div className="mono" style={{ fontSize: 12, color: 'var(--ink-3)', marginTop: 2 }}>
            {item.sku} · {t('đơn', 'order')} {item.order}
          </div>
        </div>
      </div>

      <div style={{ display: 'flex', alignItems: 'flex-end', gap: 18 }}>
        <div>
          <div className="lbl">{t('Vị trí', 'Bin')}</div>
          <div className="mono" style={{ fontSize: 22, fontWeight: 600 }}>
            {item.bin}
          </div>
        </div>
        <div>
          <div className="lbl">{t('Số lượng', 'Qty')}</div>
          <div className="mono tnum" style={{ fontSize: 22, fontWeight: 600 }}>
            ×{item.qty}
          </div>
        </div>
        <span style={{ flex: 1 }} />
        {!item.done && (
          <button
            type="button"
            className="btn lg"
            onClick={onCannotFind}
            style={{
              borderColor: 'var(--warn-line)',
              color: 'var(--warn-ink)',
              background: 'var(--warn-soft)',
            }}
          >
            <AlertTriangle size={16} strokeWidth={1.75} aria-hidden />
            {t('Không tìm thấy', 'Not found')}
          </button>
        )}
      </div>
    </div>
  );
}

// ── "Cannot find" reason modal ──────────────────────────────────────────────

function CannotFindModal({ item, onClose }: { item: PickItem; onClose: () => void }) {
  const [reason, setReason] = useState('');

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
        aria-label={t('Không tìm thấy SKU', 'SKU not found')}
        style={{
          position: 'fixed',
          left: '50%',
          bottom: 0,
          transform: 'translateX(-50%)',
          width: 'min(520px, 100vw)',
          background: 'var(--panel)',
          borderTop: '4px solid var(--warn-line)',
          borderTopLeftRadius: 'var(--radius-lg)',
          borderTopRightRadius: 'var(--radius-lg)',
          zIndex: 31,
        }}
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
          <AlertTriangle
            size={16}
            strokeWidth={1.75}
            style={{ color: 'var(--warn-ink)' }}
            aria-hidden
          />
          <span style={{ flex: 1, fontSize: 14, fontWeight: 600 }}>
            {t('Không tìm thấy SKU', 'SKU not found')}
          </span>
          <button
            className="btn ghost sm"
            type="button"
            onClick={onClose}
            aria-label={t('Đóng', 'Close')}
          >
            <X size={14} aria-hidden />
          </button>
        </div>

        <div style={{ padding: 18 }}>
          <div style={{ fontSize: 14, fontWeight: 600 }}>{item.name}</div>
          <div className="mono" style={{ fontSize: 12, color: 'var(--ink-3)', marginTop: 2 }}>
            {item.sku} · {t('vị trí', 'bin')} {item.bin}
          </div>

          <div style={{ marginTop: 14, fontSize: 12.5, color: 'var(--ink-2)', lineHeight: 1.55 }}>
            {t(
              'Kích hoạt bù trừ: giải phóng giữ chỗ, phân bổ lại sang kênh, ghi miễn trừ cho nhân viên.',
              'Triggers compensation: release the reservation, re-allocate to channels, log an exception for the operator.',
            )}
          </div>

          <fieldset style={{ border: 'none', padding: 0, margin: '16px 0 0' }}>
            <legend className="lbl" style={{ marginBottom: 8 }}>
              {t('Lý do · bắt buộc', 'Reason · required')}
            </legend>
            <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
              {CANNOT_FIND_REASONS.map((r) => {
                const label = t(r.vi, r.en);
                const selected = reason === label;
                return (
                  <label
                    key={r.en}
                    style={{
                      display: 'flex',
                      alignItems: 'center',
                      gap: 12,
                      padding: '12px 14px',
                      border: `1px solid ${selected ? 'var(--accent-line)' : 'var(--line)'}`,
                      borderRadius: 'var(--radius-lg)',
                      cursor: 'pointer',
                      background: selected ? 'var(--accent-soft)' : 'transparent',
                    }}
                  >
                    <input
                      type="radio"
                      name="cannot-find-reason"
                      checked={selected}
                      onChange={() => setReason(label)}
                    />
                    <span style={{ fontSize: 15 }}>{label}</span>
                  </label>
                );
              })}
            </div>
          </fieldset>
        </div>

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
          <button className="btn primary" type="button" disabled={reason === ''} onClick={onClose}>
            <Pill kind="warn" style={{ marginRight: 2 }}>
              {t('bù trừ', 'compensate')}
            </Pill>
            {t('Báo cáo và bỏ qua', 'Report & skip')}
          </button>
        </div>
      </div>
    </Fragment>
  );
}
