/**
 * useFilterSearchParams — Sprint-7.5 plan U7 / KTD5.
 *
 * Generic helper hook for persisting list-route state (filter, sort, page,
 * drawer-open SKU, ledger cursor, …) in URL search params via TanStack
 * Router's `validateSearch` + `useSearch` + `useNavigate` primitives.
 *
 * Adoption pattern (every list-route in the app converges on this shape so
 * future modules — Inbound, Channel, Analytics, Settings — pick it up by
 * copy-paste from one of the existing call sites; see AE7):
 *
 * ```ts
 * // 1. Declare the schema on the route via `validateSearch`. Invalid params
 * //    fall back to defaults rather than throwing.
 * export const Route = createFileRoute('/_auth/inventory')({
 *   validateSearch: (raw): InventorySearch => ({
 *     filter:   raw.filter   === 'lowStock' ? 'lowStock' : undefined,
 *     sort:     raw.sort     === 'sku'       ? 'sku'       : undefined,
 *     page:     typeof raw.page === 'number' ? raw.page : undefined,
 *     selected: typeof raw.selected === 'string' ? raw.selected : undefined,
 *     ledger:   typeof raw.ledger   === 'string' ? raw.ledger   : undefined,
 *   }),
 *   component: InventoryRouteComponent,
 * });
 *
 * // 2. Consume from the route component (or any descendant).
 * function InventoryRouteComponent() {
 *   const [search, setSearch] = useFilterSearchParams<InventorySearch>(
 *     INVENTORY_DEFAULTS,
 *     {
 *       from: '/_auth/inventory',
 *       // Changing any of these clears `page` and `ledger` automatically.
 *       resetOn: ['filter', 'sort'],
 *       pageKey: 'page',
 *       ledgerKey: 'ledger',
 *     },
 *   );
 *
 *   // setSearch({ filter: 'lowStock' });   // also resets page + clears ledger
 *   // setSearch({ selected: undefined });  // closes the drawer (clears ledger too)
 * }
 * ```
 *
 * Default-value omission rule (per KTD5): if a key's incoming value matches
 * its default the key is removed from the URL. This keeps the URL short and
 * makes back-button traversal predictable — visits with no overrides round-
 * trip to the bare path. Setting a key to `undefined` is shorthand for
 * "reset to default".
 *
 * Filter / sort reset convention (per post-doc-review D-006): any update
 * touching one of the `resetOn` keys clears `pageKey` AND `ledgerKey`. This
 * is enforced by the hook so individual call sites never duplicate the rule.
 *
 * Built-in support for the drawer-open seam (per KTD5): the helper has no
 * special knowledge of `selected` / `ledger` names — pass them via
 * `ledgerKey` so the reset rule clears them, and write `undefined` to the
 * drawer-open key to close. The LedgerDrawer component reads its open state
 * directly from `?selected=` and emits `setSearch({ selected: undefined,
 * ledger: undefined })` on close. U6 (next round) wires the ledger cursor
 * consumer on top of this seam without rewriting the helper.
 */

import { useCallback } from 'react';
import { useNavigate, useSearch } from '@tanstack/react-router';

export interface UseFilterSearchParamsOptions<TSchema extends Record<string, unknown>> {
  /**
   * Route-id path passed to TanStack Router's `useSearch({ from })` /
   * `useNavigate({ from })`. Strict mode preserves type narrowing through
   * the schema declared via `validateSearch`. The hook accepts a `string`
   * literal so call sites stay decoupled from the generated route-tree.
   */
  from?: string;
  /**
   * Keys whose change should auto-reset `pageKey` AND clear `ledgerKey`.
   * Conventionally: filter keys + sort key. Pass an empty array to disable
   * the reset behaviour for routes whose URL state has no pagination.
   */
  resetOn?: ReadonlyArray<keyof TSchema>;
  /**
   * Name of the pagination key, reset to its default value whenever any
   * `resetOn` key changes. Omit when the route has no page state.
   */
  pageKey?: keyof TSchema;
  /**
   * Name of the ledger-cursor key, cleared (set to default) whenever any
   * `resetOn` key changes. Omit when the route has no ledger-cursor state
   * (e.g. Orders list — its detail route handles cursors separately).
   */
  ledgerKey?: keyof TSchema;
}

/**
 * Read + write a typed URL-search-params object backed by TanStack Router.
 *
 * @returns A `[values, setValues]` tuple where `values` is `defaults` merged
 *   with whatever the URL currently carries, and `setValues` accepts a
 *   `Partial<TSchema>` patch. The patch is applied to the current values,
 *   default-equal keys are omitted from the URL, and the reset-on rule
 *   clears page + ledger when applicable.
 */
export function useFilterSearchParams<TSchema extends Record<string, unknown>>(
  defaults: TSchema,
  options: UseFilterSearchParamsOptions<TSchema> = {},
): [TSchema, (updates: Partial<TSchema>) => void] {
  // The `from` option lives behind `as any` because TanStack Router's
  // `useSearch` is keyed on the generated route-tree (typed in the
  // `Register` module-augmentation). The generic helper deliberately stays
  // route-tree-agnostic so future module list pages can drop it in without
  // a type-rename pass on the helper itself.
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const rawSearch = useSearch({ from: options.from as any, strict: false }) as unknown;
  // eslint-disable-next-line @typescript-eslint/no-explicit-any
  const navigate = useNavigate({ from: options.from as any });

  // Merge defaults with whatever the URL currently carries. The validator
  // on the route already coerced unknown / malformed inputs to undefined,
  // so anything present here is schema-shaped — but we still apply the
  // default for keys absent from the URL.
  const fromUrl: Partial<TSchema> = isPlainRecord(rawSearch) ? (rawSearch as Partial<TSchema>) : {};

  const current: TSchema = mergeWithDefaults(defaults, fromUrl);

  const setValues = useCallback(
    (updates: Partial<TSchema>) => {
      const resetTriggered = (options.resetOn ?? []).some(
        (key) => key in updates && !isSameValue(updates[key], current[key]),
      );

      // Build the next object: current overlay updates overlay (optional)
      // pagination + ledger reset. Apply the reset BEFORE the explicit
      // updates so a caller can override the reset (e.g., a "go to filter
      // X page 3" deep-link still wins).
      let next: TSchema = { ...current };

      if (resetTriggered) {
        if (options.pageKey != null) {
          next = { ...next, [options.pageKey]: defaults[options.pageKey] };
        }
        if (options.ledgerKey != null) {
          next = { ...next, [options.ledgerKey]: defaults[options.ledgerKey] };
        }
      }

      next = { ...next, ...updates };

      // Build the URL-shaped object: drop any key whose value equals its
      // default. Use a fresh object literal so the navigate call receives
      // a plain record and not a `TSchema` instance retaining default keys.
      const urlShape: Record<string, unknown> = {};
      (Object.keys(next) as (keyof TSchema)[]).forEach((key) => {
        const value = next[key];
        const defaultValue = defaults[key];
        if (value === undefined) return;
        if (isSameValue(value, defaultValue)) return;
        urlShape[key as string] = value;
      });

      void navigate({
        // The reducer returns a fresh URL-shaped record (it intentionally
        // ignores the previous search — default-equal keys were already
        // dropped above). TanStack Router types the `search` reducer against
        // the route-tree-specific search shape resolved from `from`; this
        // helper stays route-tree-agnostic (see the `as any` on `from`
        // above), so the reducer is cast to match the installed router's
        // expected signature. Runtime behaviour is unchanged.
        // eslint-disable-next-line @typescript-eslint/no-explicit-any
        search: (() => urlShape) as any,
        replace: false,
      });
    },
    [current, defaults, navigate, options.resetOn, options.pageKey, options.ledgerKey],
  );

  return [current, setValues];
}

// ── Internals ────────────────────────────────────────────────────────────

function isPlainRecord(v: unknown): v is Record<string, unknown> {
  return typeof v === 'object' && v !== null && !Array.isArray(v);
}

/**
 * Shallow equality covering primitives + null/undefined parity. URL search
 * params are JSON-shaped so deep-equality is rarely needed; nested objects
 * are intentionally compared by reference identity (callers shouldn't ship
 * those in URL state anyway). If a future schema legitimately needs deep
 * equality, swap this for `@tanstack/router-core`'s `deepEqual`.
 */
function isSameValue(a: unknown, b: unknown): boolean {
  if (a === b) return true;
  // Match treating undefined ~~ matching default-undefined keys.
  if (a == null && b == null) return true;
  return false;
}

function mergeWithDefaults<TSchema extends Record<string, unknown>>(
  defaults: TSchema,
  fromUrl: Partial<TSchema>,
): TSchema {
  const out = { ...defaults };
  (Object.keys(fromUrl) as (keyof TSchema)[]).forEach((key) => {
    const v = fromUrl[key];
    if (v !== undefined) {
      out[key] = v as TSchema[typeof key];
    }
  });
  return out;
}
