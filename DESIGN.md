# Design

Visual system for ShopFlow WMS. Identity: **calm precision instrument** — a dense, high-contrast operations tool that recedes so the data leads. Anchored on Stripe Dashboard restraint and Retool ops-density. See `PRODUCT.md` for strategy.

## Theme

- **Light, true/cool-neutral.** No warm tint. The body is a cool off-white at near-zero chroma; surfaces are pure white; sunken regions a cool gray. Physical scene: operators reading dense tables under bright warehouse/office light for long shifts, and owners scanning state at a desk. Calm and legible beats moody.
- **Color strategy: Restrained.** Tinted-neutral surfaces + one accent used ≤10%. Color is spent almost entirely on semantic status (ok / warn / bad / info) and per-channel identity — not decoration.
- **Border-based elevation.** Structure comes from 1px hairline borders and background steps, not shadows. Shadows are reserved for true overlays (modal, drawer, dropdown, toast).

## Color Palette

OKLCH-composed, expressed as hex tokens in `web/src/tokens/tokens.css`. Token names are stable; screens consume the legacy aliases (`--bg`, `--ink`, `--accent`, …) so the ramp re-skins the whole app at once.

**Neutral (cool):**
- `--neutral-0` `#fbfbfc` app background · `--neutral-50` `#f4f5f7` sunken · `--neutral-100` `#eceef2`
- `--neutral-200` `#e4e7ec` hairline · `--neutral-300` `#cdd2db` strong line / faint ink
- `--neutral-400` `#98a1b0` · `--neutral-500` `#667085` tertiary ink (AA ~4.9:1 on white)
- `--neutral-600` `#475467` secondary ink · `--neutral-800` `#1d2433` primary ink · `--neutral-900` `#101322`
- Surface (panel) is `#ffffff`.

**Accent — indigo-blue (≤10%; focus, primary action, selected):**
- `--primary-500` `#4263eb` · `--primary-600` `#3651c9` (accent text, AA ~6:1 on white) · `--primary-400` `#7c93f5` · `--primary-100` `#e8edfd` (selected/soft bg)

**Semantic status** (icon/label always accompanies color — never color-alone):
- ok `#067647` / soft `#dcfae6` · warn `#b54708` / `#fef0c7` · bad `#d92d20` / `#fee4e2` · info `#1570ef` / `#d1e9ff`

**Channels keep real brand identity** (external marks): Shopee `#ee4d2d`, Lazada `#2f2a95`, TikTok `#161823`, Shopify `#1a7f4d`, Sendo `#d23f3a`.

**Contrast:** body ≥4.5:1, large/UI ≥3:1. Known recheck: placeholder/`--ink-4` faint text must not drop below 4.5:1 where it carries meaning.

## Typography

- **IBM Plex Sans** (UI/body) + **IBM Plex Mono** (numbers, codes, SKUs, IDs), self-hosted via `@fontsource`. Plex is precise and technical — on-brand for an instrument; kept deliberately, not slop.
- **Tabular numerals everywhere** (`font-variant-numeric: tabular-nums`) so columns align and in-place changes don't jump.
- Dense scale: base 14px / 13px UI; xs 11px for dense table meta; steps up to 38px display (used sparingly — no shouting heroes). Hierarchy via weight (400/500/600/700) + scale, not decoration.
- Vietnamese runs ~1.5–2× longer than English: components must tolerate wrap/truncate (`.tr`, `.nb`, `.flex-1-min`). Test labels in VI at every breakpoint.

## Components

- **Tables are the primary surface**, not card grids. Dense rows, hairline row separation, sticky headers, right-aligned tabular numbers, hover + selected (`--primary-100`) states. Retool/Stripe density.
- **No identical card grids, no hero-metric template.** KPIs read as a compact stat row or inline figures, set in type hierarchy, not boxed.
- **Pills/badges** carry status: `--ok/-soft`, `--warn/-soft`, `--bad/-soft`, `--info/-soft`; channel dots use brand colors. Always paired with a label or icon.
- **Overlays** (Modal, Drawer, Toast, dropdown) are the only shadowed elements; semantic z-index scale (`--z-dropdown` → `--z-toast`).
- **Buttons:** verb+object labels. Primary = indigo fill; secondary = hairline; ghost for low-emphasis. Focus-visible = 2px indigo outline + offset.

## Layout

- App shell: persistent left nav (role-gated) + content. Content is a single column of aligned regions, not a card mosaic.
- Flex for 1D, Grid for 2D. Responsive table-to-stacked behavior for tablet/handheld operator screens; targets ≥44px on touch.
- Vary vertical rhythm with the 4px spacing scale (`--s-1`…`--s-10`); avoid uniform gaps. Radii are tight (2–8px) — a precision instrument, not a friendly rounded SaaS.

## Motion

- Purposeful and quiet. `--duration-fast 150ms` / `--duration-medium 250ms`, `--ease-out cubic-bezier(0.2,0,0,1)`. No bounce/elastic.
- Animate transform/opacity (+ optional blur/backdrop for overlays); never layout properties.
- `prefers-reduced-motion: reduce` → crossfade or instant on every transition. List entrances may stagger; never gate content visibility on a class-triggered reveal.

## Bans (carried from impeccable)

Warm-cream/sand bg, ochre-as-primary, gradient text, glassmorphism-by-default, colored side-stripe borders, all-caps tracked eyebrows on every section, 01/02/03 numbered scaffolding, hero-metric template, identical icon-card grids.
