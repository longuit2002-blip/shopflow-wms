/**
 * CreateSkuModal — Sprint-6 plan U12 (R11).
 *
 * Owner opens this from the FilterStrip's "Thêm SKU" CTA to create a new
 * SKU. The Sprint-6 backend `CreateSkuCommand` ships with only `Sku` +
 * `InitialAvailable` fields (Inventory.Application/Commands/CreateSkuCommand.cs);
 * the plan-listed extras (name, category, threshold, price, cost, alloc)
 * wait for Sprint-7's schema expansion (Sprint-6 trade-off #1). This
 * modal therefore collects only what the backend persists — fewer
 * fields beats a misleading "fill 8 inputs that get silently discarded".
 *
 * Validation (client-side):
 *   - sku: regex `^[A-Z0-9]+(-[A-Z0-9]+)*$`, max 40 chars. Lowercase or
 *     punctuation → inline error.
 *   - initialAvailable: non-negative integer.
 *
 * Server-side errors:
 *   - 409 Conflict → inline error on the sku field ("SKU đã tồn tại").
 *   - 4xx other → inline error on the form (best-effort body parse).
 *   - 5xx → error toast pushed by the hook; modal stays open.
 *
 * On success the modal closes, the SKU list query is invalidated by the
 * hook, and a success toast surfaces. Reusing the Modal + Toast
 * primitives from U11 keeps this unit purely composition.
 */

import { useState, useId, type FormEvent } from 'react';
import { Modal } from '../primitives/Modal';
import { useInventoryMutations } from '../../hooks/useInventoryMutations';
import { ApiError } from '../../api/httpClient';
import { t, useLocale } from '../../hooks/useLocale';

export interface CreateSkuModalProps {
  isOpen: boolean;
  onClose: () => void;
}

const SKU_REGEX = /^[A-Z0-9]+(-[A-Z0-9]+)*$/;
const SKU_MAX_LENGTH = 40;
const INITIAL_AVAILABLE_MAX = 1_000_000;

interface FieldErrors {
  sku?: string;
  initialAvailable?: string;
}

export function CreateSkuModal({ isOpen, onClose }: CreateSkuModalProps) {
  useLocale();
  const [sku, setSku] = useState('');
  const [initialAvailable, setInitialAvailable] = useState('');
  const [errors, setErrors] = useState<FieldErrors>({});
  const skuId = useId();
  const initId = useId();
  const { createSku } = useInventoryMutations();

  function reset(): void {
    setSku('');
    setInitialAvailable('');
    setErrors({});
    createSku.reset();
  }

  function handleClose(): void {
    if (createSku.isPending) return;
    reset();
    onClose();
  }

  function validate(): FieldErrors {
    const next: FieldErrors = {};
    const trimmedSku = sku.trim();
    if (trimmedSku.length === 0) {
      next.sku = t('SKU không được để trống.', 'SKU is required.');
    } else if (trimmedSku.length > SKU_MAX_LENGTH) {
      next.sku = t(
        `SKU dài tối đa ${SKU_MAX_LENGTH} ký tự.`,
        `SKU is at most ${SKU_MAX_LENGTH} characters.`,
      );
    } else if (!SKU_REGEX.test(trimmedSku)) {
      next.sku = t(
        'SKU phải là chữ HOA + số + dấu gạch ngang.',
        'SKU must be uppercase letters, digits, and hyphens only.',
      );
    }

    const parsedInitial = Number.parseInt(initialAvailable, 10);
    if (initialAvailable.trim() === '') {
      next.initialAvailable = t('Tồn ban đầu không được để trống.', 'Initial total is required.');
    } else if (Number.isNaN(parsedInitial) || parsedInitial < 0) {
      next.initialAvailable = t(
        'Tồn ban đầu phải ≥ 0.',
        'Initial total must be ≥ 0.',
      );
    } else if (parsedInitial > INITIAL_AVAILABLE_MAX) {
      next.initialAvailable = t(
        `Tồn ban đầu tối đa ${INITIAL_AVAILABLE_MAX.toLocaleString('vi-VN')}.`,
        `Initial total is at most ${INITIAL_AVAILABLE_MAX.toLocaleString('en-US')}.`,
      );
    }
    return next;
  }

  function extractConflictMessage(err: unknown): string | undefined {
    if (!(err instanceof ApiError)) return undefined;
    if (err.status !== 409) return undefined;
    return t('SKU đã tồn tại.', 'SKU already exists.');
  }

  async function handleSubmit(e: FormEvent<HTMLFormElement>): Promise<void> {
    e.preventDefault();
    const next = validate();
    setErrors(next);
    if (Object.keys(next).length > 0) return;
    try {
      await createSku.mutateAsync({
        sku: sku.trim(),
        initialAvailable: Number.parseInt(initialAvailable, 10),
      });
      reset();
      onClose();
    } catch (err) {
      const conflictMessage = extractConflictMessage(err);
      if (conflictMessage) {
        setErrors({ sku: conflictMessage });
      }
      // 5xx surfaces via the error toast pushed by the hook; modal stays open.
    }
  }

  return (
    <Modal
      isOpen={isOpen}
      onClose={handleClose}
      title={t('Thêm SKU mới', 'Add new SKU')}
      dismissOnBackdrop={false}
      footer={
        <>
          <button
            type="button"
            className="btn ghost"
            onClick={handleClose}
            disabled={createSku.isPending}
          >
            {t('Hủy', 'Cancel')}
          </button>
          <button
            type="submit"
            form="create-sku-form"
            className="btn primary"
            disabled={createSku.isPending}
            data-testid="create-sku-submit"
          >
            {createSku.isPending
              ? t('Đang tạo…', 'Creating…')
              : t('Tạo SKU', 'Create SKU')}
          </button>
        </>
      }
    >
      <form
        id="create-sku-form"
        onSubmit={handleSubmit}
        style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-4)' }}
        noValidate
      >
        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-1)' }}>
          <label htmlFor={skuId} className="lbl">
            {t('Mã SKU', 'SKU code')}
          </label>
          <input
            id={skuId}
            type="text"
            value={sku}
            onChange={(e) => setSku(e.target.value.toUpperCase())}
            placeholder="YN-RED-100, …"
            maxLength={SKU_MAX_LENGTH}
            autoComplete="off"
            spellCheck={false}
            aria-invalid={errors.sku ? true : undefined}
            aria-describedby={errors.sku ? `${skuId}-err` : `${skuId}-help`}
            data-testid="create-sku-sku"
            style={{
              width: '100%',
              fontFamily: 'var(--font-mono)',
              textTransform: 'uppercase',
            }}
          />
          {errors.sku ? (
            <div
              id={`${skuId}-err`}
              role="alert"
              className="t-xs"
              style={{ color: 'var(--bad-ink)' }}
              data-testid="create-sku-sku-error"
            >
              {errors.sku}
            </div>
          ) : (
            <div id={`${skuId}-help`} className="t-xs" style={{ color: 'var(--ink-2)' }}>
              {t(
                'Chữ HOA + số + dấu gạch ngang. Tối đa 40 ký tự.',
                'Uppercase + digits + hyphens. Max 40 characters.',
              )}
            </div>
          )}
        </div>

        <div style={{ display: 'flex', flexDirection: 'column', gap: 'var(--s-1)' }}>
          <label htmlFor={initId} className="lbl">
            {t('Tồn ban đầu', 'Initial total')}
          </label>
          <input
            id={initId}
            type="number"
            min={0}
            max={INITIAL_AVAILABLE_MAX}
            inputMode="numeric"
            autoComplete="off"
            spellCheck={false}
            value={initialAvailable}
            onChange={(e) => setInitialAvailable(e.target.value)}
            placeholder="0, 100, 1000, …"
            aria-invalid={errors.initialAvailable ? true : undefined}
            aria-describedby={errors.initialAvailable ? `${initId}-err` : undefined}
            data-testid="create-sku-initial"
            style={{ width: '100%' }}
          />
          {errors.initialAvailable ? (
            <div
              id={`${initId}-err`}
              role="alert"
              className="t-xs"
              style={{ color: 'var(--bad-ink)' }}
              data-testid="create-sku-initial-error"
            >
              {errors.initialAvailable}
            </div>
          ) : null}
        </div>

        <div className="t-xs" style={{ color: 'var(--ink-3)' }}>
          {t(
            'Tên, danh mục, mức an toàn, giá, phân bổ kênh sẽ bổ sung ở Sprint-7.',
            'Name, category, threshold, price, and channel allocation arrive in Sprint-7.',
          )}
        </div>
      </form>
    </Modal>
  );
}
