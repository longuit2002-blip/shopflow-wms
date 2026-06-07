# Product

## Register

product

## Users

- **Floor operators** (Picker, Packer, Dispatcher): work fast, often on tablets or handhelds on the warehouse floor, long shifts, glance-and-act. The primary task on any given screen is a single hand-off action (confirm pick / pack / ship) under time pressure. Correctness matters: a wrong action is an oversell or a mis-ship.
- **Owner / workspace admin**: desk-based; oversees inventory health, order throughput, channel sync, role/permission and tenant administration. Reads dashboards, reconciles exceptions, manages users.
- Context: Southeast-Asia marketplace sellers (Shopee / Lazada / TikTok Shop / Shopify). Multi-tenant; bilingual EN/VI. High data density — numbers are the content, not decoration around it.

## Product Purpose

ShopFlow WMS keeps marketplace stock correct across channels and moves orders through a Reserve → Pick → Pack → Ship fulfillment flow without overselling. It exists because SEA multi-channel sellers lose money to oversell and slow hand-offs. Success means: operators act on the right item in one glance, owners see real stock / order / channel state, and no order is oversold. This is a real production app, not a portfolio demo — every screen is wired to live data or an honest empty/loading/error state.

## Brand Personality

Precise, calm, dependable. The interface is an instrument, not a brochure: it recedes so the data leads. Confident under density, never decorative. Voice is plain, specific, and operational — verb+object labels, exact numbers, no marketing tone.

## Anti-references

- The warm-cream / sand body background plus amber-ochre accent (the current palette) — the saturated AI default of 2026. Removed.
- Identical card grids and the hero-metric template (big number, small label, gradient accent).
- Glassmorphism, gradient text, decorative blur, colored side-stripe borders.
- All-caps tracked eyebrows above every section; 01 / 02 / 03 numbered scaffolding.
- Generic friendly-SaaS sameness — rounded pastel cards that could belong to any B2B tool.

## Design Principles

1. **Data leads, chrome recedes.** The most ink goes to the numbers and statuses operators act on; structural chrome stays quiet.
2. **One glance, one action.** Each operator screen makes the next correct action obvious and hard to get wrong.
3. **Density without noise.** High information per screen, organized by hierarchy and alignment, not boxed into cards.
4. **Status is a first-class signal.** Color is spent almost entirely on semantic state (ok / warn / bad / info / per-channel), not on decoration.
5. **Real over mock.** Every surface shows live data or an honest empty / loading / error state. No placeholder content ships.
6. **Correctness is visible.** Reserved / available / oversell-risk and saga state stay legible; ambiguity is surfaced, not hidden.

## Accessibility & Inclusion

- WCAG 2.1 AA: body text ≥ 4.5:1, large and UI text ≥ 3:1, placeholders held to the same bar. Re-verify against the new neutral ramp (the prior ochre-on-cream and muted-gray steps are a known contrast risk).
- Bilingual EN/VI — layouts and components must absorb Vietnamese's longer strings without overflow.
- `prefers-reduced-motion` honored on every transition (crossfade or instant fallback).
- Tabular numerals wherever numbers align in columns or change in place.
- Operator screens are touch-friendly (targets ≥ 44px on tablet/handheld); desk and admin flows are keyboard-first.
- Status is never encoded by color alone — always icon or label plus color, for color-vision deficiency.
