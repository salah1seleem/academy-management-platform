"use client";
import Link from "next/link";
import { FormEvent, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { formatDateTimeAr, formatMoneyAr, planTypeLabel } from "../lib/formatters";
import { csrfRequest } from "./dashboard-api";

type Item = { id: string; arabicName: string };
type Version = { revision: number; arabicName: string; price: string; currency: string; planType: string; durationDays?: number; sessionCount?: number; published: boolean; effectiveAtUtc: string };
type Plan = { arabicName: string; englishName?: string; sportId: string; sport?: string; planType: string; price: string | number; currency: string; durationDays?: number; sessionCount?: number; displayOrder: number; allBranches: boolean; branchIds: string[]; revision?: number; versions?: Version[] };
const blank: Plan = { arabicName: "", sportId: "", planType: "Duration", price: "", currency: "EGP", displayOrder: 0, allBranches: true, branchIds: [] };

export function PlanForm({ id, readOnly = false }: { id?: string; readOnly?: boolean }) {
  const router = useRouter(); const [plan, setPlan] = useState<Plan>(blank);
  const [options, setOptions] = useState<{ sports: Item[]; branches: Item[] }>({ sports: [], branches: [] });
  const [loading, setLoading] = useState(true); const [restricted, setRestricted] = useState(false); const [busy, setBusy] = useState(false); const [error, setError] = useState("");
  useEffect(() => {
    const controller = new AbortController();
    void Promise.all([fetch("/api/v1/manage/structure/options", { signal: controller.signal }), fetch("/api/v1/my-access", { signal: controller.signal }), id ? fetch(`/api/v1/subscriptions/plans/${id}`, { signal: controller.signal }) : Promise.resolve(null)]).then(async ([o, a, p]) => {
      if (!o.ok || !a.ok || p && !p.ok) throw new Error("تعذر تحميل الباقة والفروع المتاحة.");
      const structure = await o.json(), access = await a.json(), value = p ? await p.json() : { ...blank, allBranches: access.allBranches !== false };
      if (!controller.signal.aborted) { setOptions(structure); setRestricted(access.allBranches === false); setPlan(value); }
    }).catch(e => { if (!controller.signal.aborted) setError(e instanceof Error ? e.message : "تعذر تحميل الباقة."); }).finally(() => { if (!controller.signal.aborted) setLoading(false); });
    return () => controller.abort();
  }, [id]);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (busy) return; setBusy(true); setError("");
    const form = new FormData(event.currentTarget);
    const body = { arabicName: form.get("arabicName"), englishName: form.get("englishName") || null, sportId: plan.sportId,
      planType: plan.planType === "Duration" ? 1 : plan.planType === "Sessions" ? 2 : 3, price: String(form.get("price")),
      durationDays: plan.planType === "Sessions" ? null : Number(form.get("durationDays")), sessionCount: plan.planType === "Duration" ? null : Number(form.get("sessionCount")),
      displayOrder: plan.displayOrder, allBranches: plan.allBranches, branchIds: plan.allBranches ? [] : plan.branchIds, expectedRevision: plan.revision ?? null };
    try { await csrfRequest(id ? `/api/v1/subscriptions/plans/${id}` : "/api/v1/subscriptions/plans", id ? "PUT" : "POST", body); router.push("/dashboard/subscriptions/plans"); }
    catch (e) { setError(e instanceof Error ? e.message : "تعذر حفظ الباقة."); } finally { setBusy(false); }
  }
  return <><PageHeader title={readOnly ? "عرض الباقة" : id ? "تعديل الباقة" : "إضافة باقة"} context="الأعضاء والاشتراكات / الباقات والخدمات" description="حدد الخدمة والسعر والفروع. يحتفظ كل طلب اشتراك بشروطه وقت التسجيل." />
    {loading ? <LoadingState /> : <>{error && <ErrorState message={error} />}{readOnly ? <section className="detail-card"><h2>{plan.arabicName}</h2><p>{plan.sport} · {planTypeLabel(plan.planType)} · {formatMoneyAr(plan.price, plan.currency)}</p><p>{plan.allBranches ? "متاحة في كل الفروع" : `متاحة في ${plan.branchIds.length} فرع`}</p><Link className="primary-action" href={`/dashboard/subscriptions/plans/${id}/edit`}>تعديل</Link></section> : <form className="form-shell plan-editor" onSubmit={e => void submit(e)}>
      <h2>بيانات الخدمة</h2><label>الاسم العربي<input name="arabicName" required maxLength={160} defaultValue={plan.arabicName} /></label><label>الاسم الإنجليزي — اختياري<input name="englishName" maxLength={160} defaultValue={plan.englishName ?? ""} /></label>
      <label>الرياضة<select required value={plan.sportId} onChange={e => setPlan({ ...plan, sportId: e.target.value })}><option value="">اختر الرياضة</option>{options.sports.map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select></label>
      <label>نوع الخدمة<select value={plan.planType} onChange={e => setPlan({ ...plan, planType: e.target.value })}><option value="Duration">بالمدة</option><option value="Sessions">بالحصص</option><option value="Combined">بالمدة والحصص</option></select></label>
      <label>السعر ({plan.currency})<input name="price" type="number" inputMode="decimal" min="0.01" max="999999999" step="0.01" required defaultValue={plan.price} /></label>
      {plan.planType !== "Sessions" && <label>عدد الأيام<input name="durationDays" type="number" min="1" max="3660" required defaultValue={plan.durationDays ?? ""} /></label>}
      {plan.planType !== "Duration" && <label>عدد الحصص<input name="sessionCount" type="number" min="1" max="10000" required defaultValue={plan.sessionCount ?? ""} /></label>}
      <fieldset className="plan-publication"><legend>الفروع المتاحة</legend>{!restricted && <label className="plan-branch-choice"><input type="checkbox" checked={plan.allBranches} onChange={e => setPlan({ ...plan, allBranches: e.target.checked })} />كل فروع الأكاديمية</label>}
        {!plan.allBranches && <div className="plan-branch-grid">{options.branches.map(x => <label className="plan-branch-choice" key={x.id}><input type="checkbox" checked={plan.branchIds.includes(x.id)} onChange={e => setPlan({ ...plan, branchIds: e.target.checked ? [...plan.branchIds, x.id] : plan.branchIds.filter(b => b !== x.id) })} />{x.arabicName}</label>)}</div>}
        {!plan.allBranches && plan.branchIds.length === 0 && <p className="form-hint">اختر فرعًا واحدًا على الأقل لنشر الباقة.</p>}
      </fieldset><div className="form-actions"><button className="primary-button" disabled={busy || !plan.allBranches && plan.branchIds.length === 0}>{busy ? "جارٍ الحفظ…" : "حفظ الباقة"}</button><Link href="/dashboard/subscriptions/plans">إلغاء</Link></div>
    </form>}
    {id && <section className="detail-card"><h2>تاريخ الأسعار وشروط الخدمة</h2><p className="form-hint">كل نسخة محفوظة لا تُعدّل. السجلات الأقدم من V2 لا تُنسب إلى نسخة تاريخية غير موثقة.</p>{plan.versions?.length ? <div className="plan-version-list">{plan.versions.map(v => <article key={v.revision}><strong>نسخة {v.revision} · {formatMoneyAr(v.price, v.currency)}</strong><span>{v.arabicName} · {planTypeLabel(v.planType)}{v.durationDays ? ` · ${v.durationDays} يوم` : ""}{v.sessionCount ? ` · ${v.sessionCount} حصة` : ""}</span><small>{formatDateTimeAr(v.effectiveAtUtc)} · {v.published ? "منشورة" : "موقوفة"}</small></article>)}</div> : <p>لا توجد نسخ منشورة بعد. سيحفظ التعديل التالي أول نسخة موثقة.</p>}</section>}</>}
  </>;
}
