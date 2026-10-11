"use client";
import { FormEvent, useCallback, useEffect, useState } from "react";
import { ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { formatDateTimeAr, formatMoneyAr } from "../lib/formatters";
import { csrfRequest } from "./dashboard-api";

type Item = { id: string; arabicName: string };
type Rule = Item & { type?: "Percentage" | "FixedAmount"; percentage: number; fixedAmount?: string; maximumAmount?: string; currency?: string; reason: string; isSiblingOffer: boolean; isActive: boolean; branchId?: string; sportId?: string; subscriptionPlanId?: string; startsOn?: string; endsOn?: string; priority?: number; revision?: number };
type Options = { owner: boolean; restricted: boolean; branches: Item[]; sports: Item[]; plans: (Item & { sportId: string; allBranches: boolean; branchIds: string[] })[] };
type Version = { revision: number; createdAtUtc: string; snapshot: Rule };
const blank: Rule = { id: "", arabicName: "", type: "Percentage", percentage: 25, isSiblingOffer: false, isActive: true, reason: "" };
export function discountLabel(rule: Rule) { return rule.type === "FixedAmount" ? formatMoneyAr(rule.fixedAmount ?? "0", rule.currency ?? "EGP") : `${rule.percentage}%`; }

export function DiscountManagement() {
  const [rules, setRules] = useState<Rule[]>([]); const [options, setOptions] = useState<Options | null>(null);
  const [edit, setEdit] = useState<Rule | null>(null); const [history, setHistory] = useState<{ id: string; rows: Version[] } | null>(null);
  const [error, setError] = useState(""); const [busy, setBusy] = useState(false);
  const load = useCallback(() => Promise.all([fetch("/api/v1/discount-rules/"), fetch("/api/v1/discount-rules/options")])
    .then(async ([r, o]) => { if (!r.ok || !o.ok) throw new Error(); setRules((await r.json()).items); setOptions(await o.json()); })
    .catch(() => setError("تعذر تحميل الخصومات ونطاقها. أعد المحاولة.")), []);
  useEffect(() => { void load(); }, [load]);
  async function showHistory(id: string) { setError(""); try { const r = await fetch(`/api/v1/discount-rules/${id}/versions`); if (!r.ok) throw new Error(); setHistory({ id, rows: await r.json() }); } catch { setError("تعذر تحميل سجل العرض."); } }
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (!edit || busy) return; const f = new FormData(event.currentTarget); setBusy(true); setError("");
    const fixed = edit.type === "FixedAmount";
    try {
      await csrfRequest(edit.id ? `/api/v1/discount-rules/${edit.id}` : "/api/v1/discount-rules", edit.id ? "PUT" : "POST", {
        arabicName: f.get("name"), type: fixed ? 2 : 1, percentage: fixed ? "0" : String(f.get("value")), fixedAmount: fixed ? String(f.get("value")) : null,
        maximumAmount: f.get("maximum") || null, isSiblingOffer: edit.isSiblingOffer, isActive: edit.isSiblingOffer || f.get("active") === "on", reason: f.get("reason") || null,
        branchId: edit.branchId || null, sportId: edit.sportId || null, subscriptionPlanId: edit.subscriptionPlanId || null,
        startsOn: f.get("startsOn") || null, endsOn: f.get("endsOn") || null, priority: Number(f.get("priority") || 0), expectedRevision: edit.revision ?? null,
      }); setEdit(null); setHistory(null); await load();
    } catch (e) { setError(e instanceof Error ? e.message : "تعذر حفظ الخصم."); } finally { setBusy(false); }
  }
  const header = <PageHeader title="إدارة الخصومات" context="الباقات والخصومات" description="عرض واحد مستحق لكل عملية، محسوب على الخادم ومحفوظ مع الإيصال." />;
  if (!options) return <>{header}{error ? <ErrorState message={error} /> : <LoadingState />}</>;
  if (edit) return <>{header}<form className="form-shell discount-editor" onSubmit={e => void save(e)} key={edit.id}>
    <h2>{edit.id ? "تعديل العرض" : "إضافة عرض"}</h2>
    <label>اسم الخصم<input name="name" required maxLength={120} defaultValue={edit.arabicName} readOnly={edit.isSiblingOffer} /></label>
    <label>نوع الخصم<select value={edit.type ?? "Percentage"} onChange={e => setEdit({ ...edit, type: e.target.value as Rule["type"] })}><option value="Percentage">نسبة مئوية</option><option value="FixedAmount">قيمة ثابتة</option></select></label>
    <label>{edit.type === "FixedAmount" ? "قيمة الخصم" : "النسبة المئوية"}<input key={edit.type} type="number" inputMode="decimal" name="value" required min="0.01" max={edit.type === "FixedAmount" ? "999999999" : "100"} step="0.01" defaultValue={edit.type === "FixedAmount" ? edit.fixedAmount : edit.percentage} /></label>
    <label>وصف العرض — اختياري<textarea name="reason" maxLength={500} defaultValue={edit.reason} /></label>
    <label>الفرع<select required={options.restricted} value={edit.branchId ?? ""} onChange={e => setEdit({ ...edit, branchId: e.target.value, subscriptionPlanId: "" })}><option value="">{options.restricted ? "اختر الفرع" : "كل الفروع"}</option>{options.branches.map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select></label>
    <label>الرياضة<select value={edit.sportId ?? ""} onChange={e => setEdit({ ...edit, sportId: e.target.value, subscriptionPlanId: "" })}><option value="">كل الرياضات</option>{options.sports.map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select></label>
    <label>الباقة<select value={edit.subscriptionPlanId ?? ""} onChange={e => setEdit({ ...edit, subscriptionPlanId: e.target.value })}><option value="">كل الباقات المتوافقة</option>{options.plans.filter(x => (!edit.sportId || x.sportId === edit.sportId) && (!edit.branchId || x.allBranches || x.branchIds.includes(edit.branchId))).map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select></label>
    <label>أقصى خصم — اختياري<input name="maximum" type="number" min="0.01" max="999999999" step="0.01" inputMode="decimal" defaultValue={edit.maximumAmount ?? ""} /></label>
    <label>يسري من — اختياري<input name="startsOn" type="date" defaultValue={edit.startsOn ?? ""} /></label>
    <label>يسري حتى — اختياري<input name="endsOn" type="date" defaultValue={edit.endsOn ?? ""} /></label>
    <label>أولوية التعادل<input name="priority" type="number" min="0" max="10000" step="1" defaultValue={edit.priority ?? 0} /></label>
    {!edit.isSiblingOffer && <label className="discount-active"><input type="checkbox" name="active" defaultChecked={edit.isActive} />العرض فعال</label>}
    <p className="form-hint">يُطبّق الأعلى منفعة، دون جمع عروض. عند التعادل يفوز رقم الأولوية الأصغر. الخصم اليدوي على عملية مالية يظل بحاجة إلى سبب.</p>
    {error && <ErrorState message={error} />}<div className="form-actions"><button className="primary-button" disabled={busy}>{busy ? "جارٍ الحفظ…" : "حفظ العرض"}</button><button type="button" className="secondary-button" disabled={busy} onClick={() => { setEdit(null); setError(""); }}>إلغاء</button></div>
  </form></>;
  const sibling = rules.find(r => r.isSiblingOffer) ?? { ...blank, arabicName: "عرض الإخوة", percentage: 20, isSiblingOffer: true, reason: "عرض الإخوة الافتراضي" };
  return <>{header}{error && <ErrorState message={error} />}<section className="detail-card discount-summary"><div><span className="eyebrow">خصم العائلة</span><h2>{sibling.arabicName}</h2><p>للطفل الإضافي عندما يكون لأحد إخوته اشتراك مدفوع فعال.</p></div><strong className="metric">{discountLabel(sibling)}</strong>{options.owner && <button className="secondary-button" onClick={() => setEdit(sibling)}>تعديل عرض الإخوة</button>}</section>
    <section className="detail-card"><div className="discount-toolbar"><h2>المدارس والعروض</h2><button className="primary-button" onClick={() => setEdit({ ...blank })}>إضافة خصم</button></div>
      <div className="linked-list discount-rule-list">{rules.map(r => <article key={r.id}><div><strong>{r.arabicName} · {discountLabel(r)}</strong><small>{r.isActive ? "فعال" : "متوقف"} · {options.branches.find(b => b.id === r.branchId)?.arabicName ?? "كل الفروع"}{r.maximumAmount ? ` · بحد أقصى ${formatMoneyAr(r.maximumAmount, r.currency)}` : ""} · نسخة {r.revision ?? 0}</small></div><div className="discount-actions">{(!r.isSiblingOffer || options.owner) && (!options.restricted || !!r.branchId) && <button className="secondary-button" aria-label={`تعديل ${r.arabicName}`} onClick={() => setEdit(r)}>تعديل</button>}<button className="secondary-button" onClick={() => void showHistory(r.id)} aria-label={`سجل ${r.arabicName}`}>السجل</button></div></article>)}</div>
    </section>{history && <section className="detail-card"><h2>سجل نسخ العرض</h2><p>نسخ محفوظة لا تتغير بأثر رجعي.</p><div className="linked-list">{history.rows.map(v => <article key={v.revision}><div><strong>نسخة {v.revision} · {v.snapshot.arabicName} · {discountLabel(v.snapshot)}</strong><small>{formatDateTimeAr(v.createdAtUtc)} · {v.snapshot.isActive ? "فعال" : "متوقف"}</small>{v.snapshot.reason && <small>{v.snapshot.reason}</small>}</div></article>)}</div><button className="secondary-button" onClick={() => setHistory(null)}>إغلاق السجل</button></section>}</>;
}

export function PlayerDiscounts({ playerId }: { playerId: string }) {
  const [rules, setRules] = useState<Rule[]>([]); const [links, setLinks] = useState<string[]>([]); const [message, setMessage] = useState(""); const [busy, setBusy] = useState(false);
  useEffect(() => { Promise.all([fetch("/api/v1/discount-rules/"), fetch(`/api/v1/discount-rules/players/${playerId}`)]).then(async ([r, l]) => { if (!r.ok || !l.ok) throw new Error(); setRules((await r.json()).items); setLinks(await l.json()); }).catch(() => setMessage("تعذر تحميل خصومات اللاعب.")); }, [playerId]);
  async function toggle(rule: Rule) { if (busy) return; setBusy(true); const active = !links.includes(rule.id); try { await csrfRequest(`/api/v1/discount-rules/players/${playerId}`, "PUT", { discountRuleId: rule.id, isActive: active }); setLinks(active ? [...links, rule.id] : links.filter(id => id !== rule.id)); setMessage("تم تحديث الخصم المستمر للتجديدات المقبلة."); } catch (e) { setMessage(e instanceof Error ? e.message : "تعذر تحديث الخصم."); } finally { setBusy(false); } }
  return <section className="detail-card"><h2>خصومات اللاعب المستمرة</h2><p>يظل العرض مرتبطًا بالملف؛ الطلبات المالية السابقة لا تتغير بأثر رجعي.</p>{rules.filter(r => !r.isSiblingOffer && r.isActive).map(r => <label key={r.id}><input type="checkbox" disabled={busy} checked={links.includes(r.id)} onChange={() => void toggle(r)} />{r.arabicName} · {discountLabel(r)}</label>)}{message && <p role="status">{message}</p>}</section>;
}
