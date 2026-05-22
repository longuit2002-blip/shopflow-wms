import { Fragment, useMemo, useState } from 'react';
import { Button } from '../primitives/Button';
import { t, useLocale } from '../../hooks/useLocale';
import {
  MODULES,
  ORPHAN_KEYS,
  PERMISSION_KEYS,
  type EditableRole,
  type RolePermissions,
} from '../../api/admin';

/**
 * Sprint-9.5 U7 — Owner-only admin editor for per-role permission
 * grants (R25/R26). 3-column grid (Owner / Picker / Dispatcher) × 24
 * permission keys grouped by module.
 *
 * KTD11 visualization — Owner column is read-only with a lock icon +
 * `aria-describedby` tooltip per row. Server-side KTD13 OwnerCritical
 * guard (RolePermissionsCommandHandler) remains authoritative; this
 * client lock is UX consistency.
 *
 * Save flow:
 *   1. Diff vs initial → confirmation modal shows added/removed per role.
 *   2. On Continue → fire one PUT per modified role via `onSave(role,
 *      keys)`.
 *   3. Toast "Permissions updated. Changes propagate within 15 minutes."
 *      raised by the caller (R5 propagation hint).
 *
 * No optimistic updates per R27 — caller re-fetches after Save success.
 */
export interface RolePermissionsEditorProps {
  initial: RolePermissions;
  canEdit: boolean;
  onSave: (role: EditableRole, keys: readonly string[]) => Promise<void> | void;
}

export function RolePermissionsEditor({ initial, canEdit, onSave }: RolePermissionsEditorProps) {
  useLocale();
  const [picker, setPicker] = useState<Set<string>>(() => new Set(initial.Picker));
  const [dispatcher, setDispatcher] = useState<Set<string>>(() => new Set(initial.Dispatcher));
  const [collapsed, setCollapsed] = useState<Record<string, boolean>>({});
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const diffs = useMemo(() => {
    const initPicker = new Set(initial.Picker);
    const initDispatcher = new Set(initial.Dispatcher);
    return {
      picker: {
        added: [...picker].filter((k) => !initPicker.has(k)),
        removed: [...initPicker].filter((k) => !picker.has(k)),
      },
      dispatcher: {
        added: [...dispatcher].filter((k) => !initDispatcher.has(k)),
        removed: [...initDispatcher].filter((k) => !dispatcher.has(k)),
      },
    };
  }, [initial, picker, dispatcher]);

  const hasDiff =
    diffs.picker.added.length
    + diffs.picker.removed.length
    + diffs.dispatcher.added.length
    + diffs.dispatcher.removed.length
    > 0;

  function togglePicker(key: string) {
    setPicker((s) => {
      const next = new Set(s);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }
  function toggleDispatcher(key: string) {
    setDispatcher((s) => {
      const next = new Set(s);
      if (next.has(key)) next.delete(key);
      else next.add(key);
      return next;
    });
  }

  async function handleContinueSave() {
    setSubmitting(true);
    setError(null);
    try {
      if (diffs.picker.added.length + diffs.picker.removed.length > 0) {
        await onSave('Picker', [...picker]);
      }
      if (diffs.dispatcher.added.length + diffs.dispatcher.removed.length > 0) {
        await onSave('Dispatcher', [...dispatcher]);
      }
      setConfirmOpen(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err));
    } finally {
      setSubmitting(false);
    }
  }

  const ownerSet = new Set(initial.Owner);

  return (
    <div data-testid="role-permissions-editor" style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-4)' }}>
      <div style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between' }}>
        <h1 className="t-xl" style={{ margin: 0, fontWeight: 600 }}>
          {t('Quyền theo vai trò', 'Role permissions')}
        </h1>
        <Button
          type="button"
          variant="primary"
          size="md"
          disabled={!canEdit || !hasDiff || submitting}
          onClick={() => setConfirmOpen(true)}
        >
          {t('Lưu thay đổi', 'Save changes')}
        </Button>
      </div>

      <p className="t-sm" style={{ margin: 0, color: 'var(--ink-2)' }}>
        {t(
          'Quyền của Owner không thể chỉnh sửa (KTD13). Thay đổi quyền sẽ lan ra trong vòng 15 phút.',
          'Owner permissions cannot be edited (KTD13). Permission changes propagate within 15 minutes.',
        )}
      </p>

      <table style={{ width: '100%', borderCollapse: 'collapse' }}>
        <thead>
          <tr>
            <th scope="col" style={th}>
              {t('Quyền', 'Permission')}
            </th>
            <th scope="col" style={th}>
              {t('Owner (khóa)', 'Owner (locked)')}
            </th>
            <th scope="col" style={th}>
              Picker
            </th>
            <th scope="col" style={th}>
              Dispatcher
            </th>
          </tr>
        </thead>
        <tbody>
          {MODULES.map((module) => {
            const moduleKeys = PERMISSION_KEYS.filter((p) => p.module === module);
            if (moduleKeys.length === 0) return null;
            const isCollapsed = collapsed[module] === true;
            return (
              <Fragment key={module}>
                <tr style={{ background: 'var(--bg-soft)' }}>
                  <td
                    colSpan={4}
                    style={{
                      padding: 'var(--s-2) var(--s-3)',
                      fontWeight: 600,
                      cursor: 'pointer',
                    }}
                    onClick={() => setCollapsed((c) => ({ ...c, [module]: !c[module] }))}
                  >
                    {isCollapsed ? '▸' : '▾'} {module}{' '}
                    <span style={{ color: 'var(--ink-3)', fontWeight: 400, fontSize: 12 }}>
                      ({moduleKeys.length})
                    </span>
                  </td>
                </tr>
                {!isCollapsed
                  && moduleKeys.map((p) => {
                    const tooltipId = `lock-${p.key}`;
                    const orphanTooltipId = `orphan-${p.key}`;
                    const isOrphan = ORPHAN_KEYS.includes(p.key);
                    return (
                      <tr key={p.key}>
                        <td style={td}>
                          <code style={{ fontSize: 13 }}>{p.key}</code>
                          {isOrphan && (
                            <span
                              aria-hidden="true"
                              title={t(
                                'Chưa gắn hành động — bật quyền này hiện không có hiệu lực.',
                                'No action attached yet — enabling this key is a no-op today but would grant any future action attached to it.',
                              )}
                              style={{ marginLeft: 6, color: 'var(--ink-3)', fontSize: 12 }}
                            >
                              {'⚠'}
                            </span>
                          )}
                        </td>
                        <td style={td}>
                          <input
                            type="checkbox"
                            disabled
                            checked={ownerSet.has(p.key)}
                            aria-label={`Owner ${p.key}`}
                            aria-describedby={tooltipId}
                          />
                          <span id={tooltipId} className="sr-only">
                            {t(
                              'Quyền của Owner không thể chỉnh sửa (KTD13).',
                              'Owner permissions cannot be edited (KTD13).',
                            )}
                          </span>
                        </td>
                        <td style={td}>
                          <input
                            type="checkbox"
                            disabled={!canEdit || isOrphan}
                            checked={picker.has(p.key)}
                            onChange={() => togglePicker(p.key)}
                            aria-label={`Picker ${p.key}`}
                            aria-describedby={isOrphan ? orphanTooltipId : undefined}
                          />
                        </td>
                        <td style={td}>
                          <input
                            type="checkbox"
                            disabled={!canEdit || isOrphan}
                            checked={dispatcher.has(p.key)}
                            onChange={() => toggleDispatcher(p.key)}
                            aria-label={`Dispatcher ${p.key}`}
                            aria-describedby={isOrphan ? orphanTooltipId : undefined}
                          />
                          {isOrphan && (
                            <span id={orphanTooltipId} className="sr-only">
                              {t(
                                'Chưa gắn hành động — bật quyền này hiện không có hiệu lực nhưng sẽ áp dụng cho bất kỳ hành động nào được gắn trong tương lai.',
                                'No action attached yet — enabling this key is a no-op today but would grant any future action attached to it.',
                              )}
                            </span>
                          )}
                        </td>
                      </tr>
                    );
                  })}
              </Fragment>
            );
          })}
        </tbody>
      </table>

      {confirmOpen && (
        <div
          role="dialog"
          aria-modal="true"
          aria-label={t('Xác nhận lưu', 'Confirm save')}
          style={{
            position: 'fixed',
            inset: 0,
            background: 'rgba(0,0,0,0.4)',
            display: 'flex',
            alignItems: 'center',
            justifyContent: 'center',
            zIndex: 100,
          }}
        >
          <div
            className="card"
            style={{ width: 480, padding: 'var(--s-6)', display: 'flex', flexDirection: 'column', gap: 'var(--s-3)' }}
          >
            <h2 className="t-lg" style={{ margin: 0 }}>{t('Xác nhận thay đổi quyền', 'Confirm permission changes')}</h2>
            {(['picker', 'dispatcher'] as const).map((role) => {
              const d = diffs[role];
              if (d.added.length === 0 && d.removed.length === 0) return null;
              return (
                <section key={role}>
                  <h3 className="t-md" style={{ margin: 0 }}>
                    {role === 'picker' ? 'Picker' : 'Dispatcher'}
                  </h3>
                  {d.added.length > 0 && (
                    <p className="t-sm" style={{ margin: '4px 0', color: 'var(--ok-fg)' }}>
                      + {d.added.join(', ')}
                    </p>
                  )}
                  {d.removed.length > 0 && (
                    <p className="t-sm" style={{ margin: '4px 0', color: '#7A1A1A' }}>
                      − {d.removed.join(', ')}
                    </p>
                  )}
                </section>
              );
            })}
            {error && (
              <div role="alert" className="t-sm" style={{ color: '#7A1A1A' }}>
                {error}
              </div>
            )}
            <div style={{ display: 'flex', gap: 'var(--s-2)', justifyContent: 'flex-end' }}>
              <Button
                type="button"
                variant="secondary"
                size="md"
                onClick={() => {
                  setConfirmOpen(false);
                  setError(null);
                }}
                disabled={submitting}
              >
                {t('Hủy', 'Cancel')}
              </Button>
              <Button
                type="button"
                variant="primary"
                size="md"
                disabled={submitting}
                onClick={handleContinueSave}
              >
                {submitting ? t('Đang lưu…', 'Saving…') : t('Tiếp tục', 'Continue')}
              </Button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

const th = {
  padding: 'var(--s-3)',
  textAlign: 'left' as const,
  borderBottom: '1px solid var(--border)',
  fontWeight: 500,
  fontSize: 13,
  color: 'var(--ink-2)',
};
const td = {
  padding: 'var(--s-2) var(--s-3)',
  borderBottom: '1px solid var(--border-soft)',
};
