"use client";
import Link from "next/link";
import { FormEvent, useEffect, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { ErrorState, LoadingState, PageHeader, StatusBadge } from "../components/dashboard-shell";
import { formatDateTimeAr, formatMoneyAr, planTypeLabel } from "../lib/formatters";
import { csrfRequest } from "./dashboard-api";
import { planCatalogHref, planCatalogPath, planLimits, planServiceLabels as services, planTrainingFormats as formats } from "./plan-catalog-values";
import { useStaffActionAccess } from "./use-staff-action";
import styles from "./plan-editor.module.css";

type Item = { id: string; arabicName: string };
type Version = { revision: number; arabicName: string; price: string; currency: string; planType: string; durationDays?: number; durationMonths?: number; sessionCount?: number; published: boolean; effectiveAtUtc: string };
type Plan = { arabicName: string; englishName?: string; sportId: string; sport?: string; planType: string; price: string | number; currency: string; durationDays?: number; durationMonths?: number; sessionCount?: number; serviceKind?: string; trainingFormat?: string; referenceMonthlyPrice?: string; chargedMonths?: number; displayOrder: number; allBranches: boolean; branchIds: string[]; isActive?: boolean; revision?: number; versions?: Version[]; canManagePublication?: boolean };
type Structure = { sports: Item[]; branches: Item[] };
const emptyStructure: Structure = { sports: [], branches: [] };
const blank: Plan = { arabicName: "", sportId: "", planType: "Duration", serviceKind: "Training", trainingFormat: "Group", price: "", currency: "", displayOrder: 0, allBranches: true, branchIds: [] };
const maximumPrice = 999999999;
const currencyNames = new Intl.DisplayNames("ar", { type: "currency" });
export function seasonPrice(reference: string, months: number): string {
  if (!/^(?:\d{1,9}(?:\.\d{1,2})?|\.\d{1,2})$/.test(reference) || !Number.isInteger(months) || months < 1 || months > 120) return "";
  const [whole, fraction = ""] = reference.split(".");
  const hundred = BigInt(100);
  const cents = (BigInt(whole || "0") * hundred + BigInt(fraction.padEnd(2, "0"))) * BigInt(months);
  return `${cents / hundred}.${String(cents % hundred).padStart(2, "0")}`;
}

export function PlanForm({ id, readOnly = false }: { id?: string; readOnly?: boolean }) {
  return <PlanEditor key={`${id ?? "new"}:${readOnly}`} id={id} readOnly={readOnly} />;
}

function PlanEditor({ id, readOnly }: { id?: string; readOnly: boolean }) {
  const router = useRouter(); const [plan, setPlan] = useState<Plan>(blank);
  const [durationUnit, setDurationUnit] = useState<"Days" | "Months">("Days");
  const [serviceMonths, setServiceMonths] = useState("");
  const search = useSearchParams().get("q") ?? "";
  const listHref = planCatalogHref(planCatalogPath, search);
  const [revision, setRevision] = useState(0);
  const canWrite = useStaffActionAccess("subscriptions", id ? "edit" : "add", revision);
  const [options, setOptions] = useState(emptyStructure);
  const [loading, setLoading] = useState(true); const [restricted, setRestricted] = useState(false); const [busy, setBusy] = useState(false); const [error, setError] = useState("");
  const [nameInvalid, setNameInvalid] = useState(false);
  const [loadError, setLoadError] = useState<{ message: string; retry: boolean }>();
  const [loaded, setLoaded] = useState(false);
  const season = plan.serviceKind === "Training" && plan.trainingFormat === "Season";
  const referencePrice = season ? seasonPrice(plan.referenceMonthlyPrice ?? "", 1) : "";
  const seasonTotal = season ? seasonPrice(plan.referenceMonthlyPrice ?? "", plan.chargedMonths ?? 0) : "";
  const invalidReferencePrice = season && Boolean(plan.referenceMonthlyPrice) && !referencePrice;
  const totalAboveLimit = Number(seasonTotal) > maximumPrice;
  const currencyLabel = plan.currency === "EGP" ? "جنيه" : /^[A-Z]{3}$/.test(plan.currency) ? currencyNames.of(plan.currency) : "";
  const priceLabel = currencyLabel ? `السعر (${currencyLabel})` : "السعر";
  useEffect(() => {
    const controller = new AbortController();
    const request = { signal: controller.signal, cache: "no-store" as const };
    async function load() {
      const record = id ? await fetch(`/api/v1/subscriptions/plans/${id}`, request) : null;
      if (record?.status === 404 || record?.status === 403) {
        if (!controller.signal.aborted) setLoadError({ message: "الباقة غير متاحة في نطاقك.", retry: false });
        return;
      }
      if (record && !record.ok) throw new Error();
      const value: Plan | undefined = record ? await record.json() : undefined;
      if (record && (!value || typeof value.arabicName !== "string" || !Array.isArray(value.branchIds))) throw new Error();

      if (readOnly && value) {
        if (!controller.signal.aborted) { setPlan({ ...blank, ...value }); setLoaded(true); }
        if (!value.allBranches) {
          const structure = await fetch("/api/v1/manage/structure/options", request).catch(() => null);
          if (structure?.ok) {
            const data: Structure | null = await structure.json().catch(() => null);
            if (Array.isArray(data?.branches) && !controller.signal.aborted) setOptions(data);
          }
        }
        return;
      }

      if (value?.canManagePublication === false) {
        if (!controller.signal.aborted) setLoadError({ message: "تعديل هذه الباقة يحتاج صلاحية على كل فروع نشرها.", retry: false });
        return;
      }

      const [structure, access, experience] = await Promise.all([
        fetch("/api/v1/manage/structure/options", request), fetch("/api/v1/my-access", request),
        id ? null : fetch("/api/v1/academy/experience", request),
      ]);
      if (!structure.ok || !access.ok || (experience && !experience.ok)) throw new Error();
      const setup: Structure = await structure.json();
      const scope: { allBranches?: boolean } = await access.json();
      const brand: { currency?: string } | null = experience ? await experience.json() : null;
      if (!Array.isArray(setup?.branches) || !Array.isArray(setup?.sports)) throw new Error();
      if (!controller.signal.aborted) {
        setOptions(setup);
        setRestricted(scope.allBranches === false);
        setPlan({ ...blank, currency: brand?.currency ?? "", allBranches: scope.allBranches !== false, ...value });
        setDurationUnit(value?.durationMonths ? "Months" : "Days");
        setServiceMonths(value?.durationMonths ? String(value.durationMonths) : "");
        setLoaded(true);
      }
    }
    void load().catch(() => {
      if (!controller.signal.aborted) setLoadError({ message: id ? "تعذر تحميل الباقة." : "تعذر تحميل الرياضات والفروع المتاحة.", retry: true });
    }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [id, readOnly, revision]);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (busy || !loaded || canWrite !== true || totalAboveLimit || (season && !seasonTotal)) return; setError("");
    const form = new FormData(event.currentTarget);
    const arabicName = String(form.get("arabicName") ?? "").trim();
    if (!arabicName) {
      setNameInvalid(true);
      event.currentTarget.querySelector<HTMLInputElement>('input[name="arabicName"]')?.focus();
      return;
    }
    setBusy(true);
    const body = { arabicName, englishName: String(form.get("englishName") ?? "").trim() || null, sportId: plan.sportId,
      planType: plan.planType === "Duration" ? 1 : plan.planType === "Sessions" ? 2 : 3, price: season ? seasonTotal : String(form.get("price")),
      durationDays: plan.planType === "Sessions" || durationUnit === "Months" ? null : Number(form.get("durationDays")), durationMonths: plan.planType === "Sessions" || durationUnit === "Days" ? null : Number(serviceMonths), sessionCount: plan.planType === "Duration" ? null : Number(form.get("sessionCount")),
      serviceKind: Object.keys(services).indexOf(plan.serviceKind ?? "Training") + 1, trainingFormat: Object.keys(formats).indexOf(plan.trainingFormat ?? "Group") + 1,
      referenceMonthlyPrice: season ? referencePrice : null, chargedMonths: season ? plan.chargedMonths : null,
      displayOrder: plan.displayOrder, allBranches: plan.allBranches, branchIds: plan.allBranches ? [] : plan.branchIds, expectedRevision: plan.revision ?? null };
    try { await csrfRequest(id ? `/api/v1/subscriptions/plans/${id}` : "/api/v1/subscriptions/plans", id ? "PUT" : "POST", body); router.push(listHref); }
    catch (e) { setError(e instanceof Error ? e.message : "تعذر حفظ الباقة."); } finally { setBusy(false); }
  }
  function reload() {
    setLoaded(false);
    setLoading(true);
    setLoadError(undefined);
    setRevision(value => value + 1);
  }
  const visibleBranches = options.branches.filter(branch => plan.branchIds.includes(branch.id)).map(branch => branch.arabicName);
  const branches = plan.allBranches ? "كل الفروع" : visibleBranches.length > 0 ? visibleBranches.join("، ") : "فروع محددة";
  const limits = planLimits(plan);
  const limitsLabel = plan.planType === "Sessions" ? "الحصص" : plan.planType === "Duration" ? "المدة" : "المدة والحصص";
  return <><PageHeader title={readOnly ? loaded ? plan.arabicName : "تفاصيل الباقة" : id ? "تعديل الباقة" : "إضافة باقة"} context="الاشتراكات / باقات الاشتراك"
    back={{ label: "العودة إلى باقات الاشتراك", href: listHref }}
    action={readOnly && loaded && canWrite === true && plan.canManagePublication !== false ? { label: "تعديل الباقة", href: planCatalogHref(`${planCatalogPath}/${id}/edit`, search) } : undefined} />
    {loadError ? <section className="detail-card"><ErrorState message={loadError.message} />{loadError.retry && <button type="button" className="button button-secondary" onClick={reload}>إعادة التحميل</button>}</section>
    : (!loaded && loading) || (!readOnly && canWrite === null) ? <LoadingState message="جارٍ تحميل بيانات الباقة…" />
    : !readOnly && canWrite === "unavailable" ? <section className="detail-card"><ErrorState message="تعذّر التحقق من صلاحية حفظ الباقات." /><button type="button" className="button button-secondary" onClick={reload}>إعادة التحميل</button></section>
    : !readOnly && !canWrite ? <section className="detail-card"><ErrorState message="لا تملك صلاحية حفظ الباقات." /></section>
    : <>{error && <ErrorState message={error} />}{readOnly ? <section className="detail-card" aria-label="شروط الباقة الحالية">
      <div className={styles.detailHeading}><h2>شروط الباقة الحالية</h2>{typeof plan.isActive === "boolean" && <StatusBadge status={plan.isActive ? "Active" : "Inactive"} label={plan.isActive ? "متاحة" : "موقوفة"} />}</div>
      <dl className={styles.details}>
        <div><dt>السعر</dt><dd>{formatMoneyAr(plan.price, plan.currency)}</dd></div>
        <div><dt>الرياضة</dt><dd>{plan.sport}</dd></div>
        <div><dt>مجال الخدمة</dt><dd>{services[plan.serviceKind ?? "Training"]}</dd></div>
        {(plan.serviceKind ?? "Training") === "Training" && <div><dt>طريقة التدريب</dt><dd>{formats[plan.trainingFormat ?? "Group"]}</dd></div>}
        {limits && <div><dt>{limitsLabel}</dt><dd>{limits}</dd></div>}
        {season && plan.chargedMonths && <div><dt>الأشهر المدفوعة</dt><dd>{plan.chargedMonths}</dd></div>}
        {season && plan.referenceMonthlyPrice && <div><dt>السعر الشهري المرجعي</dt><dd>{formatMoneyAr(plan.referenceMonthlyPrice, plan.currency, true)}</dd></div>}
        <div><dt>الفروع</dt><dd>{branches}</dd></div>
      </dl>
    </section> : <form className="form-shell plan-editor" onSubmit={e => void submit(e)}>
      <h2>بيانات الخدمة</h2>
      <label>الاسم العربي<input name="arabicName" required maxLength={160} defaultValue={plan.arabicName} aria-invalid={nameInvalid || undefined} aria-describedby={nameInvalid ? "plan-arabic-name" : undefined} onChange={e => { if (nameInvalid && e.target.value.trim()) setNameInvalid(false); }} /></label>
      {nameInvalid && <p id="plan-arabic-name" role="alert" className={styles.fieldError}>أدخل اسم الباقة بالعربية.</p>}
      <label>الاسم الإنجليزي — اختياري<input name="englishName" maxLength={160} defaultValue={plan.englishName ?? ""} /></label>
      <label>الرياضة<select required value={plan.sportId} onChange={e => setPlan({ ...plan, sportId: e.target.value })}><option value="">اختر الرياضة</option>{options.sports.map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select></label>
      <label>مجال الخدمة<select value={plan.serviceKind} onChange={e => setPlan({ ...plan, serviceKind: e.target.value, trainingFormat: "Group" })}>{Object.entries(services).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
      {plan.serviceKind === "Training" ? <label>طريقة التدريب<select value={plan.trainingFormat} onChange={e => {
        const trainingFormat = e.target.value;
        if (trainingFormat === "Season") {
          setDurationUnit("Months");
          setServiceMonths(value => value || "5");
          setPlan({ ...plan, trainingFormat, planType: "Duration" });
        } else {
          setPlan({ ...plan, trainingFormat });
        }
      }}>{Object.entries(formats).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label> : <p className="form-hint">تظهر هذه الخدمة في مسار الحجز الخاص بها.</p>}
      {!season && <label>نظام الباقة<select value={plan.planType} onChange={e => setPlan({ ...plan, planType: e.target.value })}><option value="Duration">بالمدة</option><option value="Sessions">بالحصص</option><option value="Combined">بالمدة والحصص</option></select></label>}
      {plan.planType !== "Sessions" && <>
        {!season && <label>وحدة المدة<select value={durationUnit} onChange={e => {
          const unit = e.target.value === "Months" ? "Months" : "Days";
          setDurationUnit(unit);
          if (unit === "Months") setServiceMonths(value => value || "1");
        }}><option value="Days">أيام</option><option value="Months">أشهر تقويمية</option></select></label>}
        {durationUnit === "Months" ? <label key="months">أشهر الخدمة<input name="durationMonths" type="number" min="1" max="120" required value={serviceMonths} onChange={e => setServiceMonths(e.target.value)} /></label>
          : <label key="days">عدد الأيام<input name="durationDays" type="number" min="1" max="3660" required defaultValue={plan.durationDays ?? ""} /></label>}
      </>}
      {plan.planType !== "Duration" && <label>عدد الحصص<input name="sessionCount" type="number" min="1" max="10000" required defaultValue={plan.sessionCount ?? ""} /></label>}
      {season && <>
        <label>السعر الشهري المرجعي<input name="referenceMonthlyPrice" type="number" inputMode="decimal" min="0.01" max={maximumPrice} step="0.01" required value={plan.referenceMonthlyPrice ?? ""} aria-invalid={invalidReferencePrice || undefined} aria-describedby={invalidReferencePrice ? "plan-reference-price" : undefined} onChange={e => setPlan({ ...plan, referenceMonthlyPrice: e.target.value })} /></label>
        {invalidReferencePrice && <p id="plan-reference-price" role="status" className={styles.fieldError}>اكتب السعر كرقم عشري بحد أقصى خانتين بعد العلامة.</p>}
        <label>عدد الأشهر المدفوعة<input name="chargedMonths" type="number" min="1" max={Number(serviceMonths) || 120} required value={plan.chargedMonths ?? ""} onChange={e => setPlan({ ...plan, chargedMonths: e.target.value === "" ? undefined : Number(e.target.value) })} /></label>
      </>}
      <label>{priceLabel}{season ? <input key="season-price" name="price" readOnly value={seasonTotal} aria-invalid={totalAboveLimit || undefined} aria-describedby={totalAboveLimit ? "plan-price-limit" : undefined} />
        : <input key="regular-price" name="price" type="number" inputMode="decimal" min="0.01" max={maximumPrice} step="0.01" required value={plan.price} onChange={e => setPlan({ ...plan, price: e.target.value })} />}</label>
      {totalAboveLimit && <p id="plan-price-limit" role="status" className={styles.fieldError}>الحد الأقصى لسعر الباقة {formatMoneyAr(maximumPrice, plan.currency)}. راجع السعر الشهري أو عدد الأشهر المدفوعة.</p>}
      <fieldset className="plan-publication"><legend>الفروع المتاحة</legend>{!restricted && <label className="plan-branch-choice"><input type="checkbox" checked={plan.allBranches} onChange={e => setPlan({ ...plan, allBranches: e.target.checked })} />كل فروع الأكاديمية</label>}
        {!plan.allBranches && <div className="plan-branch-grid">{options.branches.map(x => <label className="plan-branch-choice" key={x.id}><input type="checkbox" checked={plan.branchIds.includes(x.id)} onChange={e => setPlan({ ...plan, branchIds: e.target.checked ? [...plan.branchIds, x.id] : plan.branchIds.filter(b => b !== x.id) })} />{x.arabicName}</label>)}</div>}
        {!plan.allBranches && plan.branchIds.length === 0 && <p className="form-hint">اختر فرعًا واحدًا على الأقل لنشر الباقة.</p>}
      </fieldset><div className="form-actions"><button className="primary-button" disabled={busy || invalidReferencePrice || totalAboveLimit || !plan.allBranches && plan.branchIds.length === 0}>{busy ? "جارٍ الحفظ…" : "حفظ الباقة"}</button><Link href={listHref}>إلغاء</Link></div>
    </form>}
    {id && <section className="detail-card"><h2>تاريخ الأسعار وشروط الخدمة</h2>{plan.versions?.length ? <div className="plan-version-list">{plan.versions.map(v => <article key={v.revision}><strong>نسخة {v.revision} · {formatMoneyAr(v.price, v.currency)}</strong><span>{[v.arabicName, planTypeLabel(v.planType), planLimits(v)].filter(Boolean).join(" · ")}</span><small>{formatDateTimeAr(v.effectiveAtUtc)} · {v.published ? "متاحة" : "موقوفة"}</small></article>)}</div> : <p>لا توجد تغييرات موثّقة في شروط هذه الباقة.</p>}</section>}</>}
  </>;
}
