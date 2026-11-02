"use client";
import Link from "next/link";
import { FormEvent, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { formatDateTimeAr, formatMoneyAr, planTypeLabel } from "../lib/formatters";
import { csrfRequest } from "./dashboard-api";

type Item = { id: string; arabicName: string };
type Version = { revision: number; arabicName: string; price: string; currency: string; planType: string; durationDays?: number; durationMonths?: number; sessionCount?: number; published: boolean; effectiveAtUtc: string };
type Plan = { arabicName: string; englishName?: string; sportId: string; sport?: string; planType: string; price: string | number; currency: string; durationDays?: number; durationMonths?: number; sessionCount?: number; serviceKind?: string; trainingFormat?: string; referenceMonthlyPrice?: string; chargedMonths?: number; displayOrder: number; allBranches: boolean; branchIds: string[]; revision?: number; versions?: Version[] };
const services: Record<string, string> = { Training: "تدريب", DayPass: "زيارة يومية", OpenSwim: "سباحة حرة", FacilityBooking: "حجز مرفق لشريك", Event: "بطولة أو معسكر" };
const formats: Record<string, string> = { Group: "جماعي", Private: "خاص", SemiPrivate: "شبه خاص", Season: "موسم" };
const blank: Plan = { arabicName: "", sportId: "", planType: "Duration", serviceKind: "Training", trainingFormat: "Group", price: "", currency: "EGP", displayOrder: 0, allBranches: true, branchIds: [] };
export function seasonPrice(reference: string, months: number): string {
  if (!/^\d{1,9}(\.\d{1,2})?$/.test(reference) || !Number.isInteger(months) || months < 1 || months > 120) return "";
  const [whole, fraction = ""] = reference.split("."); const hundred = BigInt(100); const cents = (BigInt(whole) * hundred + BigInt(fraction.padEnd(2, "0"))) * BigInt(months);
  return `${cents / hundred}.${String(cents % hundred).padStart(2, "0")}`;
}

export function PlanForm({ id, readOnly = false }: { id?: string; readOnly?: boolean }) {
  const router = useRouter(); const [plan, setPlan] = useState<Plan>(blank);
  const [options, setOptions] = useState<{ sports: Item[]; branches: Item[] }>({ sports: [], branches: [] });
  const [loading, setLoading] = useState(true); const [restricted, setRestricted] = useState(false); const [busy, setBusy] = useState(false); const [error, setError] = useState("");
  const season = plan.serviceKind === "Training" && plan.trainingFormat === "Season";
  useEffect(() => {
    const controller = new AbortController();
    void Promise.all([fetch("/api/v1/manage/structure/options", { signal: controller.signal }), fetch("/api/v1/my-access", { signal: controller.signal }), id ? fetch(`/api/v1/subscriptions/plans/${id}`, { signal: controller.signal }) : Promise.resolve(null)]).then(async ([o, a, p]) => {
      if (!o.ok || !a.ok || p && !p.ok) throw new Error("تعذر تحميل الباقة والفروع المتاحة.");
      const structure = await o.json(), access = await a.json(), value = p ? await p.json() : { ...blank, allBranches: access.allBranches !== false };
      if (!controller.signal.aborted) { setOptions(structure); setRestricted(access.allBranches === false); setPlan({ ...blank, ...value }); }
    }).catch(e => { if (!controller.signal.aborted) setError(e instanceof Error ? e.message : "تعذر تحميل الباقة."); }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [id]);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (busy) return; setBusy(true); setError("");
    const form = new FormData(event.currentTarget);
    const body = { arabicName: form.get("arabicName"), englishName: form.get("englishName") || null, sportId: plan.sportId,
      planType: plan.planType === "Duration" ? 1 : plan.planType === "Sessions" ? 2 : 3, price: season ? seasonPrice(plan.referenceMonthlyPrice ?? "", plan.chargedMonths ?? 0) : String(form.get("price")),
      durationDays: plan.planType === "Sessions" || plan.durationMonths ? null : Number(form.get("durationDays")), durationMonths: plan.planType === "Sessions" || !plan.durationMonths ? null : Number(form.get("durationMonths")), sessionCount: plan.planType === "Duration" ? null : Number(form.get("sessionCount")),
      serviceKind: Object.keys(services).indexOf(plan.serviceKind ?? "Training") + 1, trainingFormat: Object.keys(formats).indexOf(plan.trainingFormat ?? "Group") + 1,
      referenceMonthlyPrice: season ? plan.referenceMonthlyPrice : null, chargedMonths: season ? plan.chargedMonths : null,
      displayOrder: plan.displayOrder, allBranches: plan.allBranches, branchIds: plan.allBranches ? [] : plan.branchIds, expectedRevision: plan.revision ?? null };
    try { await csrfRequest(id ? `/api/v1/subscriptions/plans/${id}` : "/api/v1/subscriptions/plans", id ? "PUT" : "POST", body); router.push("/dashboard/subscriptions/plans"); }
    catch (e) { setError(e instanceof Error ? e.message : "تعذر حفظ الباقة."); } finally { setBusy(false); }
  }
  return <><PageHeader title={readOnly ? "عرض الباقة" : id ? "تعديل الباقة" : "إضافة باقة"} context="الأعضاء والاشتراكات / الباقات والخدمات" description="حدد الخدمة والسعر والفروع. يحتفظ كل طلب اشتراك بشروطه وقت التسجيل." />
    {loading ? <LoadingState /> : <>{error && <ErrorState message={error} />}{readOnly ? <section className="detail-card"><h2>{plan.arabicName}</h2><p>{plan.sport} · {services[plan.serviceKind ?? "Training"]} · {planTypeLabel(plan.planType)} · {formatMoneyAr(plan.price, plan.currency)}</p>{plan.durationMonths && <p>{plan.durationMonths} شهر تقويمي{season ? ` · ${plan.chargedMonths} أشهر مدفوعة` : ""}</p>}<p>{plan.allBranches ? "متاحة في كل الفروع" : `متاحة في ${plan.branchIds.length} فرع`}</p><Link className="primary-action" href={`/dashboard/subscriptions/plans/${id}/edit`}>تعديل</Link></section> : <form className="form-shell plan-editor" onSubmit={e => void submit(e)}>
      <h2>بيانات الخدمة</h2><label>الاسم العربي<input name="arabicName" required maxLength={160} defaultValue={plan.arabicName} /></label><label>الاسم الإنجليزي — اختياري<input name="englishName" maxLength={160} defaultValue={plan.englishName ?? ""} /></label>
      <label>الرياضة<select required value={plan.sportId} onChange={e => setPlan({ ...plan, sportId: e.target.value })}><option value="">اختر الرياضة</option>{options.sports.map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select></label>
      <label>مجال الخدمة<select value={plan.serviceKind} onChange={e => setPlan({ ...plan, serviceKind: e.target.value, trainingFormat: "Group" })}>{Object.entries(services).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
      {plan.serviceKind === "Training" ? <label>طريقة التدريب<select value={plan.trainingFormat} onChange={e => setPlan({ ...plan, trainingFormat: e.target.value, ...(e.target.value === "Season" ? { durationMonths: plan.durationMonths || 5, planType: "Duration" } : {}) })}>{Object.entries(formats).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label> : <p className="form-hint">هذه خدمة مستقلة وليست اشتراك لاعب تدريب. الحجز والدخول والسعة لها مسارات تشغيل مستقلة.</p>}
      <label>نوع الخدمة<select value={plan.planType} onChange={e => setPlan({ ...plan, planType: e.target.value })} disabled={season}><option value="Duration">بالمدة</option><option value="Sessions">بالحصص</option><option value="Combined">بالمدة والحصص</option></select></label>
      {season && <><label>السعر الشهري المرجعي<input name="referenceMonthlyPrice" type="number" inputMode="decimal" min="0.01" max="999999999" step="0.01" required value={plan.referenceMonthlyPrice ?? ""} onChange={e => setPlan({ ...plan, referenceMonthlyPrice: e.target.value })} /></label><label>عدد الأشهر المدفوعة<input name="chargedMonths" type="number" min="1" max={plan.durationMonths ?? 120} required value={plan.chargedMonths ?? ""} onChange={e => setPlan({ ...plan, chargedMonths: Number(e.target.value) })} /></label></>}
      <label>السعر ({plan.currency}){season ? <input name="price" readOnly value={seasonPrice(plan.referenceMonthlyPrice ?? "", plan.chargedMonths ?? 0)} /> : <input key="regular-price" name="price" type="number" inputMode="decimal" min="0.01" max="999999999" step="0.01" required defaultValue={plan.price} />}</label>
      {plan.planType !== "Sessions" && <><label>وحدة المدة<select disabled={season} value={plan.durationMonths ? "Months" : "Days"} onChange={e => setPlan({ ...plan, durationMonths: e.target.value === "Months" ? 1 : undefined })}><option value="Days">أيام</option><option value="Months">أشهر تقويمية</option></select></label>{plan.durationMonths ? <label>أشهر الخدمة<input name="durationMonths" type="number" min="1" max="120" required value={plan.durationMonths} onChange={e => setPlan({ ...plan, durationMonths: Number(e.target.value) || 1 })} /></label> : <label>عدد الأيام<input name="durationDays" type="number" min="1" max="3660" required defaultValue={plan.durationDays ?? ""} /></label>}</>}
      {plan.planType !== "Duration" && <label>عدد الحصص<input name="sessionCount" type="number" min="1" max="10000" required defaultValue={plan.sessionCount ?? ""} /></label>}
      <fieldset className="plan-publication"><legend>الفروع المتاحة</legend>{!restricted && <label className="plan-branch-choice"><input type="checkbox" checked={plan.allBranches} onChange={e => setPlan({ ...plan, allBranches: e.target.checked })} />كل فروع الأكاديمية</label>}
        {!plan.allBranches && <div className="plan-branch-grid">{options.branches.map(x => <label className="plan-branch-choice" key={x.id}><input type="checkbox" checked={plan.branchIds.includes(x.id)} onChange={e => setPlan({ ...plan, branchIds: e.target.checked ? [...plan.branchIds, x.id] : plan.branchIds.filter(b => b !== x.id) })} />{x.arabicName}</label>)}</div>}
        {!plan.allBranches && plan.branchIds.length === 0 && <p className="form-hint">اختر فرعًا واحدًا على الأقل لنشر الباقة.</p>}
      </fieldset><div className="form-actions"><button className="primary-button" disabled={busy || !plan.allBranches && plan.branchIds.length === 0}>{busy ? "جارٍ الحفظ…" : "حفظ الباقة"}</button><Link href="/dashboard/subscriptions/plans">إلغاء</Link></div>
    </form>}
    {id && <section className="detail-card"><h2>تاريخ الأسعار وشروط الخدمة</h2><p className="form-hint">كل نسخة محفوظة لا تُعدّل. السجلات الأقدم من V2 لا تُنسب إلى نسخة تاريخية غير موثقة.</p>{plan.versions?.length ? <div className="plan-version-list">{plan.versions.map(v => <article key={v.revision}><strong>نسخة {v.revision} · {formatMoneyAr(v.price, v.currency)}</strong><span>{v.arabicName} · {planTypeLabel(v.planType)}{v.durationDays ? ` · ${v.durationDays} يوم` : ""}{v.durationMonths ? ` · ${v.durationMonths} شهر تقويمي` : ""}{v.sessionCount ? ` · ${v.sessionCount} حصة` : ""}</span><small>{formatDateTimeAr(v.effectiveAtUtc)} · {v.published ? "منشورة" : "موقوفة"}</small></article>)}</div> : <p>لا توجد نسخ منشورة بعد. سيحفظ التعديل التالي أول نسخة موثقة.</p>}</section>}</>}
  </>;
}
