/**
 * Filter strip — Sprint-6 plan U9.
 *
 * Sprint-6 ships a search box only — the wider filter set (category,
 * channel, state, zone) waits for Sprint-7 when those columns land in
 * the schema. The strip layout matches STYLING_SPECS §2.2 so future
 * filters can be added without churn.
 */

import { Search, Plus } from 'lucide-react';
import { Button } from '../primitives/Button';
import { t, useLocale } from '../../hooks/useLocale';

export interface FilterStripProps {
  search: string;
  onSearchChange: (value: string) => void;
  onCreateSkuClick?: () => void;
}

export function FilterStrip({ search, onSearchChange, onCreateSkuClick }: FilterStripProps) {
  useLocale();
  return (
    <div
      className="strip"
      style={{
        display: 'flex',
        alignItems: 'center',
        gap: 'var(--s-3)',
        padding: 'var(--s-3) var(--s-6)',
      }}
    >
      <label
        htmlFor="inventory-search"
        style={{
          position: 'relative',
          display: 'inline-flex',
          alignItems: 'center',
          flex: '0 1 320px',
        }}
      >
        <Search
          size={13}
          aria-hidden
          style={{
            position: 'absolute',
            left: 8,
            color: 'var(--ink-3)',
          }}
        />
        <input
          id="inventory-search"
          type="search"
          value={search}
          onChange={(e) => onSearchChange(e.target.value)}
          placeholder={t('Tìm SKU…', 'Search SKU…')}
          style={{ paddingLeft: 28, width: '100%' }}
          aria-label={t('Tìm SKU', 'Search SKU')}
        />
      </label>

      <span style={{ flex: 1 }} />

      {onCreateSkuClick && (
        <Button variant="primary" onClick={onCreateSkuClick}>
          <Plus size={13} aria-hidden />
          {t('Thêm SKU', 'New SKU')}
        </Button>
      )}
    </div>
  );
}
