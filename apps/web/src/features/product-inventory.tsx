"use client";

import { useEffect, useRef, useState } from "react";
import { ApprovedWorkspace } from "../components/approved-workspace";
import { ConfirmationDialog, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { canonicalDigits } from "../lib/number-input";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { useStaffActionAccess } from "./use-staff-action";
import styles from "./product-inventory.module.css";

type Variant = { id?: string; size: string; color?: string; shirtNumber: number | null; quantity: number };
type Inventory = { usesVariants: boolean; numberedUniform: boolean; stockRevision: number; stockQuantity: number; variants: Variant[] };
type Draft = { key: string; size: string; color: string; shirtNumber: string; quantity: string; identityLocked: boolean };
type Command = { expectedRevision: number; numberedUniform: boolean; reconcileExistingStock: true; variants: Variant[] };
const maximumStock = 1_000_000;
const format = new Intl.NumberFormat("ar-EG");
const reviewLabel = "راجعت الرصيد الفعلي وتوزيعه على المقاسات والأرقام.";

function integer(value: string, max: number): number | null {
  return /^\d+$/.test(value) && Number(value) <= max ? Number(value) : null;
}

function variantIdentity(variant: Pick<Variant, "size" | "color" | "shirtNumber">) {
  return JSON.stringify([(variant.color ?? "").trim(), variant.size.trim().toUpperCase(), variant.shirtNumber]);
}

function matches(inventory: Inventory, command: Command) {
  if (!inventory.usesVariants || inventory.numberedUniform !== command.numberedUniform || inventory.variants.length !== command.variants.length) return false;
  const expected = new Map(command.variants.map(variant => [variantIdentity(variant), variant.quantity]));
  return inventory.variants.every(variant => expected.get(variantIdentity(variant)) === variant.quantity);
}

async function inventoryJson(response: Response): Promise<Inventory> {
  const value: Inventory | null = await response.json().catch(() => null);
  if (!value || typeof value.usesVariants !== "boolean" || typeof value.numberedUniform !== "boolean" ||
      !Number.isSafeInteger(value.stockRevision) || !Number.isSafeInteger(value.stockQuantity) || !Array.isArray(value.variants)) {
    throw new Error("تعذر قراءة بيانات المخزون.");
  }
  return value;
}

async function readInventory(path: string, signal?: AbortSignal) {
  const response = await fetch(path, { cache: "no-store", signal });
  if (response.status === 404 || response.status === 403) throw new DashboardRequestError("مخزون هذا المنتج غير متاح في نطاقك.", response.status);
  if (!response.ok) throw new Error("تعذر تحميل المخزون.");
  return inventoryJson(response);
}

export function ProductInventoryEditor({ id }: { id: string }) {
  return <ProductInventoryForm key={id} id={id} />;
}

function ProductInventoryForm({ id }: { id: string }) {
  const productPath = `/api/v1/content/catalog/${id}`;
  const path = `${productPath}/inventory`;
  const canEdit = useStaffActionAccess("catalog", "edit");
  const [productName, setProductName] = useState("");
  const [saved, setSaved] = useState<Inventory>();
  const [rows, setRows] = useState<Draft[]>([]);
  const [numbered, setNumbered] = useState(false);
  const [reviewed, setReviewed] = useState(false);
  const [dirty, setDirty] = useState(false);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [reloadDialog, setReloadDialog] = useState(false);
  const [mustReload, setMustReload] = useState(false);
  const [pending, setPending] = useState<Command>();
  const nextRow = useRef(0);

  function adopt(data: Inventory) {
    setSaved(data);
    setNumbered(data.numberedUniform);
    const variants = data.variants.length ? data.variants : [{ size: "", shirtNumber: null, quantity: data.stockQuantity }];
    setRows(variants.map((variant, index) => ({
      key: variant.id ?? `initial-${index}`, size: variant.size, color: variant.color ?? "",
      shirtNumber: variant.shirtNumber?.toString() ?? "", quantity: variant.quantity.toString(),
      identityLocked: data.usesVariants && variant.quantity > 0,
    })));
    setReviewed(false);
    setDirty(false);
    setMustReload(false);
    setPending(undefined);
  }

  useEffect(() => {
    const controller = new AbortController();
    void readInventory(path, controller.signal).then(data => {
      if (!controller.signal.aborted) adopt(data);
    }).catch((error: unknown) => {
      if (!controller.signal.aborted) setError(error instanceof DashboardRequestError ? error.message : "تعذر تحميل المخزون.");
    }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [path]);

  useEffect(() => {
    const controller = new AbortController();
    void fetch(productPath, { cache: "no-store", signal: controller.signal }).then(response => response.ok ? response.json() : null).then(data => {
      if (!controller.signal.aborted && typeof data?.arabicName === "string") setProductName(data.arabicName);
    }).catch(() => {});
    return () => controller.abort();
  }, [productPath]);

  const variants = rows.map(row => ({
    size: row.size.trim().toUpperCase(), color: row.color.trim(),
    shirtNumber: numbered ? integer(row.shirtNumber, 999) : null, quantity: integer(row.quantity, maximumStock),
  }));
  const validQuantities = variants.every(variant => variant.quantity !== null);
  const total = variants.reduce((sum, variant) => sum + (variant.quantity ?? 0), 0);
  const duplicate = new Set(variants.filter(variant => variant.size).map(variantIdentity)).size !== variants.filter(variant => variant.size).length;
  const validation = !rows.length ? "أضف مقاسًا واحدًا على الأقل."
    : variants.some(variant => !variant.size) ? "أدخل المقاس في كل صف."
    : !validQuantities ? "أدخل كمية صحيحة لكل صف من صفر إلى مليون قطعة."
    : numbered && variants.some(variant => variant.shirtNumber === null) ? "أدخل رقم كل قميص من صفر إلى ٩٩٩."
    : duplicate ? "يوجد تكرار في اللون والمقاس ورقم القميص."
    : total > maximumStock ? "إجمالي المخزون لا يتجاوز مليون قطعة."
    : saved && !saved.usesVariants && total !== saved.stockQuantity ? "وزّع إجمالي المخزون الحالي كاملًا على المقاسات قبل التفعيل."
    : "";
  const locked = busy || loading || mustReload || Boolean(pending);

  function changeRows(update: (current: Draft[]) => Draft[]) {
    setRows(update);
    setDirty(true);
    setReviewed(false);
    setNotice("");
  }

  function editRow(key: string, field: Partial<Draft>) {
    changeRows(current => current.map(row => row.key === key ? { ...row, ...field } : row));
  }

  async function reload() {
    if (loading || busy) return;
    setReloadDialog(false);
    setLoading(true);
    setError("");
    setNotice("");
    try { adopt(await readInventory(path)); }
    catch (error) { setError(error instanceof DashboardRequestError ? error.message : "تعذر تحديث المخزون. أعد المحاولة."); }
    finally { setLoading(false); }
  }

  async function save() {
    if (!saved || locked || !reviewed || validation || canEdit !== true) return;
    const command: Command = {
      expectedRevision: saved.stockRevision, numberedUniform: numbered, reconcileExistingStock: true,
      variants: variants.map(variant => ({ ...variant, quantity: variant.quantity! })),
    };
    setBusy(true);
    setError("");
    setNotice("");
    try {
      const response = await csrfRequest(path, "PUT", command, {}, { announce: false });
      const data = await inventoryJson(response);
      if (data.stockRevision <= command.expectedRevision || !matches(data, command)) throw new Error();
      adopt(data);
      setNotice("حُفظ توزيع المخزون.");
    } catch (error) {
      setReviewed(false);
      if (error instanceof DashboardRequestError && error.status > 0 && error.status < 500) {
        setError(error.message);
        if (error.status === 409) setMustReload(true);
      } else {
        setError("تعذر تأكيد الحفظ. تحقّق من المخزون الحالي قبل إعادة المحاولة.");
        setPending(command);
      }
    } finally { setBusy(false); }
  }

  async function verify() {
    if (!pending || busy) return;
    setBusy(true);
    try {
      const data = await readInventory(path);
      if (data.stockRevision > pending.expectedRevision && matches(data, pending)) {
        adopt(data);
        setError("");
        setNotice("التوزيع المطلوب محفوظ في المخزون الحالي.");
      } else {
        const changed = data.stockRevision !== pending.expectedRevision;
        setPending(undefined);
        setMustReload(changed);
        setError(changed ? "تغيّر المخزون. حمّل الرصيد الحالي قبل أي حفظ جديد." : "المخزون ما زال على النسخة السابقة. راجع التوزيع قبل إعادة الحفظ.");
      }
    } catch {
      setError("تعذر التحقق من المخزون الحالي. أعد التحقق.");
    } finally { setBusy(false); }
  }

  return <ApprovedWorkspace>
    <PageHeader title={productName ? `مخزون ${productName}` : "مخزون المنتج"} context="المنتجات الرياضية / المخزون"
      back={{ label: "العودة إلى تفاصيل المنتج", href: `/dashboard/content/catalog/${id}` }} />
    {error && <ErrorState message={error} />}
    {notice && <p role="status">{notice}</p>}
    {!saved ? loading ? <LoadingState message="جارٍ تحميل المخزون…" /> : <button type="button" className="button button-secondary" onClick={() => void reload()}>إعادة تحميل المخزون</button>
      : <section className={`detail-card ${styles.inventory}`} aria-labelledby="stock-heading">
        <header className={styles.header}>
          <div><h2 id="stock-heading">الرصيد حسب المقاس</h2><p>المخزون المحفوظ <strong>{format.format(saved.stockQuantity)}</strong> قطعة</p></div>
          {pending ? <button type="button" className="button button-secondary" disabled={busy} onClick={() => void verify()}>{busy ? "جارٍ التحقق…" : "تحقّق من المخزون الحالي"}</button>
            : <button type="button" className="button button-secondary" disabled={busy || loading} onClick={() => dirty ? setReloadDialog(true) : void reload()}>{loading ? "جارٍ تحميل المخزون…" : "تحديث المخزون"}</button>}
        </header>
        {canEdit === true ? <>
          <label className={styles.check}><input type="checkbox" checked={numbered} disabled={locked || saved.usesVariants} onChange={event => { setNumbered(event.target.checked); changeRows(current => current.map(row => ({ ...row, shirtNumber: "" }))); }} />زي ذو أرقام جاهزة</label>
          {!saved.usesVariants && <p>عند تفعيل المقاسات، لازم مجموع التوزيع يساوي الرصيد المحفوظ.</p>}
          {rows.some(row => row.identityLocked) && <p>لتغيير مقاس أو لون أو رقم له رصيد، احفظ كميته صفرًا أولًا.</p>}
          <div className={styles.rows}>{rows.map((row, index) => <fieldset key={row.key} disabled={locked} className={styles.variant}>
            <legend>توزيع {format.format(index + 1)}</legend>
            <label>اللون <input value={row.color} maxLength={40} disabled={row.identityLocked} onChange={event => editRow(row.key, { color: event.target.value })} /></label>
            <label>المقاس <input value={row.size} maxLength={30} disabled={row.identityLocked} onChange={event => editRow(row.key, { size: event.target.value })} /></label>
            {numbered && <label>رقم القميص <input inputMode="numeric" dir="ltr" disabled={row.identityLocked} value={row.shirtNumber} onChange={event => editRow(row.key, { shirtNumber: canonicalDigits(event.target.value) })} /></label>}
            <label>الكمية <input inputMode="numeric" dir="ltr" value={row.quantity} onChange={event => editRow(row.key, { quantity: canonicalDigits(event.target.value) })} /></label>
            {!row.identityLocked && <button type="button" className="button button-secondary" onClick={() => changeRows(current => current.filter(item => item.key !== row.key))}>إزالة الصف</button>}
          </fieldset>)}</div>
          <button type="button" className="button button-secondary" disabled={locked || rows.length >= 500} onClick={() => {
            const key = `draft-${nextRow.current++}`;
            changeRows(current => [...current, { key, size: "", color: "", shirtNumber: "", quantity: "0", identityLocked: false }]);
          }}>إضافة مقاس أو رقم</button>
          <div className={styles.review}>
            <p>إجمالي التوزيع <strong>{validQuantities ? `${format.format(total)} قطعة` : "غير مكتمل"}</strong></p>
            {validation && <p id="inventory-validation">{validation}</p>}
            <label className={styles.check}><input type="checkbox" checked={reviewed} disabled={locked || Boolean(validation)} onChange={event => setReviewed(event.target.checked)} />{reviewLabel}</label>
            <button type="button" className="button button-primary" aria-describedby={validation ? "inventory-validation" : undefined} disabled={locked || !reviewed || Boolean(validation)} onClick={() => void save()}>{busy && !pending ? "جارٍ حفظ المخزون…" : "حفظ المخزون المراجع"}</button>
          </div>
        </> : <>
          {canEdit === "unavailable" && <ErrorState message="تعذر التحقق من صلاحية تعديل المخزون." />}
          {saved.variants.length > 0 ? <ul className={styles.readOnly}>{saved.variants.map(variant => <li key={variantIdentity(variant)}>
            <span>{[variant.color, `مقاس ${variant.size}`, variant.shirtNumber == null ? "" : `رقم ${format.format(variant.shirtNumber)}`].filter(Boolean).join(" · ")}</span><strong>{format.format(variant.quantity)} قطعة</strong>
          </li>)}</ul> : <p>المخزون مسجل كرصيد إجمالي.</p>}
        </>}
      </section>}
    <ConfirmationDialog open={reloadDialog} title="تحميل المخزون الحالي؟" message="سيُستبدل التوزيع الذي لم يُحفظ بالرصيد الحالي." confirmLabel="تحميل الرصيد الحالي" onConfirm={() => void reload()} onCancel={() => setReloadDialog(false)} />
  </ApprovedWorkspace>;
}
