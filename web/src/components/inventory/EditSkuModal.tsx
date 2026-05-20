/**
 * EditSkuModal — Sprint-7.5 plan U4 (R3).
 *
 * Owner-facing modal for editing the 10-field rich SKU catalog row
 * surfaced by U3 (skus table). Mirrors CreateSkuModal's Modal-over-Drawer
 * Esc-capture + optimistic-mutation patterns from Sprint-6 KTD9 / KTD10.
 *
 * Field set (matches src/Services/Inventory/.../Domain/Catalog/Sku.cs):
 *   - sku (readonly when editing)
 *   - name *required*
 *   - category (free-text; the eventual enum/autocomplete is a
 *     Sprint-9+ decision per origin Outstanding Question)
 *   - threshold (integer ≥ 0; nullable)
 *   - weightGrams (integer ≥ 0; nullable)
 *   - dimensions length / width / height (mm; all-or-nothing per
 *     post-doc-review D-008)
 *   - description (textarea, optional)
 *   - imageUrl (URL; optional; onError fallback to placeholder)
 *   - barcode (optional; partial UNIQUE on the table — 422 maps to inline
 *     error per post-doc-review D-003)
 *   - brand (optional)
 *   - isFlashSale (checkbox — the modal accepts the field; the
 *     dedicated FlashSaleToggle still owns the per-row inline toggle
 *     UX so users don't have to open the modal just to flip flash-sale)
 *
 * Submit-in-progress UX (post-doc-review D-001): Save button disabled +
 * spinner during the mutation. On 422 with code `barcode_in_use` the
 * modal stays open with an inline error on the barcode field. On 4xx
 * other the form-level error banner shows. On 5xx the toast surfaces
 * via the hook + modal stays open for retry.
 */

import { useEffect, useId, useState, type FormEvent } from 'react';
import { Modal } from '../primitives/Modal';
import { useInventoryMutations } from '../../hooks/useInventoryMutations';
import type { SkuListItem, UpdateSkuPayload } from '../../api/inventory';
import { ApiError } from '../../api/httpClient';
import { t, useLocale } from '../../hooks/useLocale';

export interface EditSkuModalProps {
  isOpen: boolean;
  initial: SkuListItem | null;
  /** Optional pre-filled rich fields beyond SkuListItem; falls back to nulls. */
  initialRich?: Partial<UpdateSkuPayload>;
  onClose: () => void;
}

interface FieldErrors {
  name?: string;
  category?: string;
  threshold?: string;
  weightGrams?: string;
  dimensions?: string;
  imageUrl?: string;
  barcode?: string;
  form?: string;
}

const NAME_MAX = 200;
const NUMERIC_MAX = 1_000_000;
const URL_REGEX = /^https?:\/\/.+/i;

export function EditSkuModal({ isOpen, initial, initialRich, onClose }: EditSkuModalProps) {
  useLocale();
  const { editSku } = useInventoryMutations();
  const nameId = useId();
  const categoryId = useId();
  const thresholdId = useId();
  const weightId = useId();
  const lenId = useId();
  const widId = useId();
  const heiId = useId();
  const descId = useId();
  const imgId = useId();
  const barcodeId = useId();
  const brandId = useId();
  const flashId = useId();

  const [name, setName] = useState('');
  const [category, setCategory] = useState('');
  const [threshold, setThreshold] = useState('');
  const [weight, setWeight] = useState('');
  const [length, setLength] = useState('');
  const [width, setWidth] = useState('');
  const [height, setHeight] = useState('');
  const [description, setDescription] = useState('');
  const [imageUrl, setImageUrl] = useState('');
  const [barcode, setBarcode] = useState('');
  const [brand, setBrand] = useState('');
  const [isFlashSale, setIsFlashSale] = useState(false);
  const [errors, setErrors] = useState<FieldErrors>({});

  // Pre-fill on open. We re-run when the keyed SKU changes so opening the
  // modal for a different row resets to that row's data instead of
  // showing stale values from a previous edit.
  useEffect(() => {
    if (!isOpen || !initial) return;
    setName(initialRich?.name ?? initial.name ?? '');
    setCategory(initialRich?.category ?? initial.category ?? '');
    setThreshold(
      initial.threshold != null
        ? String(initial.threshold)
        : initialRich?.threshold != null
          ? String(initialRich.threshold)
          : '',
    );
    setWeight(initialRich?.weightGrams != null ? String(initialRich.weightGrams) : '');
    setLength(initialRich?.dimensions?.length != null ? String(initialRich.dimensions.length) : '');
    setWidth(initialRich?.dimensions?.width != null ? String(initialRich.dimensions.width) : '');
    setHeight(initialRich?.dimensions?.height != null ? String(initialRich.dimensions.height) : '');
    setDescription(initialRich?.description ?? '');
    setImageUrl(initialRich?.imageUrl ?? '');
    setBarcode(initialRich?.barcode ?? '');
    setBrand(initialRich?.brand ?? '');
    setIsFlashSale(initialRich?.isFlashSale ?? initial.isFlashSale ?? false);
    setErrors({});
    editSku.reset();
  }, [isOpen, initial, initialRich, editSku]);

  function handleClose(): void {
    if (editSku.isPending) return;
    onClose();
  }

  function validate(): FieldErrors {
    const next: FieldErrors = {};
    if (name.trim().length === 0) {
      next.name = t('Tên SKU không được để trống.', 'SKU name is required.');
    } else if (name.trim().length > NAME_MAX) {
      next.name = t(`Tên SKU tối đa ${NAME_MAX} ký tự.`, `SKU name max ${NAME_MAX} chars.`);
    }
    if (threshold !== '') {
      const n = Number.parseInt(threshold, 10);
      if (Number.isNaN(n) || n < 0 || n > NUMERIC_MAX) {
        next.threshold = t('Ngưỡng phải ≥ 0.', 'Threshold must be ≥ 0.');
      }
    }
    if (weight !== '') {
      const n = Number.parseInt(weight, 10);
      if (Number.isNaN(n) || n < 0 || n > NUMERIC_MAX) {
        next.weightGrams = t('Khối lượng phải ≥ 0.', 'Weight must be ≥ 0.');
      }
    }
    const dimVals = [length, width, height];
    const provided = dimVals.filter((v) => v !== '').length;
    if (provided > 0 && provided < 3) {
      next.dimensions = t(
        'Kích thước phải nhập đủ dài × rộng × cao.',
        'Provide all three of length × width × height.',
      );
    } else if (provided === 3) {
      const parsed = dimVals.map((v) => Number.parseFloat(v));
      if (parsed.some((n) => Number.isNaN(n) || n < 0)) {
        next.dimensions = t('Kích thước phải ≥ 0.', 'Dimensions must be ≥ 0.');
      }
    }
    if (imageUrl.trim() !== '' && !URL_REGEX.test(imageUrl.trim())) {
      next.imageUrl = t('URL phải bắt đầu bằng http:// hoặc https://.', 'URL must start with http:// or https://.');
    }
    return next;
  }

  function extractBarcodeConflict(err: unknown): string | undefined {
    if (!(err instanceof ApiError)) return undefined;
    if (err.status !== 422 && err.status !== 409) return undefined;
    const b = err.body as Record<string, unknown> | undefined;
    const code = typeof b?.code === 'string' ? b.code : undefined;
    if (code === 'barcode_in_use' || code === 'sku.barcode_in_use') {
      return t('Barcode đã được dùng.', 'Barcode already in use.');
    }
    return undefined;
  }

  async function handleSubmit(e: FormEvent<HTMLFormElement>): Promise<void> {
    e.preventDefault();
    if (!initial) return;
    const next = validate();
    setErrors(next);
    if (Object.keys(next).length > 0) return;

    const payload: UpdateSkuPayload = {
      name: name.trim(),
      category: category.trim() === '' ? null : category.trim(),
      threshold: threshold === '' ? null : Number.parseInt(threshold, 10),
      weightGrams: weight === '' ? null : Number.parseInt(weight, 10),
      dimensions:
        length !== '' && width !== '' && height !== ''
          ? {
              length: Number.parseFloat(length),
              width: Number.parseFloat(width),
              height: Number.parseFloat(height),
              unit: 'mm',
            }
          : null,
      description: description.trim() === '' ? null : description.trim(),
      imageUrl: imageUrl.trim() === '' ? null : imageUrl.trim(),
      barcode: barcode.trim() === '' ? null : barcode.trim(),
      brand: brand.trim() === '' ? null : brand.trim(),
      isFlashSale,
    };

    try {
      await editSku.mutateAsync({ sku: initial.sku, payload });
      onClose();
    } catch (err) {
      const barcodeMsg = extractBarcodeConflict(err);
      if (barcodeMsg) {
        setErrors({ barcode: barcodeMsg });
      } else if (err instanceof ApiError && err.status < 500) {
        setErrors({ form: t('Không lưu được. Vui lòng thử lại.', 'Could not save. Please try again.') });
      }
      // 5xx surfaces via the toast pushed by the hook; modal stays open.
    }
  }

  if (!initial) return null;

  return (
    <Modal
      isOpen={isOpen}
      onClose={handleClose}
      title={`${t('Chỉnh sửa SKU', 'Edit SKU')} · ${initial.sku}`}
      dismissOnBackdrop={false}
      footer={
        <>
          <button
            type="button"
            className="btn ghost"
            onClick={handleClose}
            disabled={editSku.isPending}
          >
            {t('Hủy', 'Cancel')}
          </button>
          <button
            type="submit"
            form="edit-sku-form"
            className="btn primary"
            disabled={editSku.isPending}
            aria-busy={editSku.isPending || undefined}
            data-testid="edit-sku-submit"
          >
            {editSku.isPending ? t('Đang lưu…', 'Saving…') : t('Lưu', 'Save')}
          </button>
        </>
      }
    >
      <form id="edit-sku-form" onSubmit={handleSubmit} noValidate>
        {errors.form ? (
          <div role="alert" data-testid="edit-sku-form-error" className="t-sm" style={{ color: 'var(--bad-ink)', marginBottom: 'var(--s-3)' }}>
            {errors.form}
          </div>
        ) : null}
        <FieldRow label={t('SKU', 'SKU')} id={`${nameId}-readonly`}>
          <input id={`${nameId}-readonly`} className="t-input" value={initial.sku} readOnly disabled />
        </FieldRow>
        <FieldRow label={t('Tên', 'Name')} id={nameId} error={errors.name}>
          <input id={nameId} className="t-input" value={name} onChange={(e) => setName(e.target.value)} maxLength={NAME_MAX} required />
        </FieldRow>
        <FieldRow label={t('Danh mục', 'Category')} id={categoryId} error={errors.category}>
          <input id={categoryId} className="t-input" value={category} onChange={(e) => setCategory(e.target.value)} />
        </FieldRow>
        <FieldRow label={t('Ngưỡng cảnh báo', 'Low-stock threshold')} id={thresholdId} error={errors.threshold}>
          <input id={thresholdId} className="t-input" type="number" min={0} value={threshold} onChange={(e) => setThreshold(e.target.value)} />
        </FieldRow>
        <FieldRow label={t('Khối lượng (g)', 'Weight (g)')} id={weightId} error={errors.weightGrams}>
          <input id={weightId} className="t-input" type="number" min={0} value={weight} onChange={(e) => setWeight(e.target.value)} />
        </FieldRow>
        <fieldset style={{ border: 0, padding: 0, margin: 0 }}>
          <legend className="lbl" style={{ marginBottom: 'var(--s-2)' }}>{t('Kích thước (mm)', 'Dimensions (mm)')}</legend>
          <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr 1fr', gap: 'var(--s-2)' }}>
            <label htmlFor={lenId} className="t-sm">{t('Dài', 'Length')}
              <input id={lenId} className="t-input" type="number" min={0} step="0.1" value={length} onChange={(e) => setLength(e.target.value)} />
            </label>
            <label htmlFor={widId} className="t-sm">{t('Rộng', 'Width')}
              <input id={widId} className="t-input" type="number" min={0} step="0.1" value={width} onChange={(e) => setWidth(e.target.value)} />
            </label>
            <label htmlFor={heiId} className="t-sm">{t('Cao', 'Height')}
              <input id={heiId} className="t-input" type="number" min={0} step="0.1" value={height} onChange={(e) => setHeight(e.target.value)} />
            </label>
          </div>
          {errors.dimensions ? (
            <div role="alert" className="t-sm" style={{ color: 'var(--bad-ink)', marginTop: 'var(--s-1)' }}>
              {errors.dimensions}
            </div>
          ) : null}
        </fieldset>
        <FieldRow label={t('Mô tả', 'Description')} id={descId}>
          <textarea id={descId} className="t-input" rows={3} value={description} onChange={(e) => setDescription(e.target.value)} />
        </FieldRow>
        <FieldRow label={t('Ảnh (URL)', 'Image URL')} id={imgId} error={errors.imageUrl}>
          <input id={imgId} className="t-input" type="url" value={imageUrl} onChange={(e) => setImageUrl(e.target.value)} />
          {imageUrl.trim() !== '' && URL_REGEX.test(imageUrl.trim()) ? (
            <img
              src={imageUrl.trim()}
              alt=""
              onError={(e) => { (e.currentTarget as HTMLImageElement).style.display = 'none'; }}
              style={{ maxHeight: 80, marginTop: 'var(--s-2)', borderRadius: 4 }}
              data-testid="edit-sku-image-preview"
            />
          ) : null}
        </FieldRow>
        <FieldRow label={t('Barcode', 'Barcode')} id={barcodeId} error={errors.barcode}>
          <input id={barcodeId} className="t-input" value={barcode} onChange={(e) => setBarcode(e.target.value)} />
        </FieldRow>
        <FieldRow label={t('Thương hiệu', 'Brand')} id={brandId}>
          <input id={brandId} className="t-input" value={brand} onChange={(e) => setBrand(e.target.value)} />
        </FieldRow>
        <label htmlFor={flashId} className="t-sm" style={{ display: 'flex', alignItems: 'center', gap: 'var(--s-2)', marginTop: 'var(--s-3)' }}>
          <input id={flashId} type="checkbox" checked={isFlashSale} onChange={(e) => setIsFlashSale(e.target.checked)} />
          {t('Đang chạy flash-sale', 'Flash-sale active')}
        </label>
      </form>
    </Modal>
  );
}

interface FieldRowProps {
  label: string;
  id: string;
  error?: string;
  children: React.ReactNode;
}

function FieldRow({ label, id, error, children }: FieldRowProps) {
  return (
    <div style={{ marginBottom: 'var(--s-3)' }}>
      <label htmlFor={id} className="lbl" style={{ display: 'block', marginBottom: 'var(--s-1)' }}>
        {label}
      </label>
      {children}
      {error ? (
        <div role="alert" className="t-sm" style={{ color: 'var(--bad-ink)', marginTop: 'var(--s-1)' }}>
          {error}
        </div>
      ) : null}
    </div>
  );
}
