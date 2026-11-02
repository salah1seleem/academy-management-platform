"use client";
import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { ConfirmationDialog, ErrorState, LoadingState } from "../components/dashboard-shell";
import { csrfRequest } from "./dashboard-api";
import { usePayrollPermission } from "./payroll-access";
import { formatDateAr, formatMoneyAr } from "../lib/formatters";
import { CoachPrivateServices } from "./coach-private-services";

export function CoachAgreementsWithServices({ coachId }: { coachId: string }) {
  return <><CoachAgreements coachId={coachId} /><CoachPrivateServices coachId={coachId} /></>;
}

type Agreement = { id: string; branchId: string; sportId: string; branch: string; sport: string; effectiveFrom: string; effectiveTo?: string; variableMethod: string; variableValue: number; eligibility: string; baseCoverage: string; currency: string; baseSalarySnapshot: number; note?: string; version?: number; approvedAtUtc?: string; approvedBy?: string };
type Data = { configured: boolean; latestApproved?: { branchId: string; sportId: string; agreementId: string; version: number }[]; employeeId?: string; monthlyBase: number; currency: string; contractEnd?: string; today: string; timeZone?: string; items: Agreement[]; branches: { id: string; name: string }[]; sports: { id: string; name: string }[] };
export function CoachAgreements({ coachId }: { coachId: string }) {
  const canManage = usePayrollPermission("payroll.agreement.manage");
  const [data, setData] = useState<Data | null>(null), [hidden, setHidden] = useState(false), [error, setError] = useState(""), [message, setMessage] = useState("");
  const [formOpen, setFormOpen] = useState(false), [busy, setBusy] = useState(false), [selected, setSelected] = useState<Agreement | null>(null), [method, setMethod] = useState("Percent");
  const keys = useRef(new Map<string, string>()); const path = `/api/v1/manage/payroll/coaches/${coachId}/agreements`;
  const load = useCallback(async (signal?: AbortSignal) => {
    const r = await fetch(path, { cache: "no-store", signal }); if (r.status === 403) { setHidden(true); return; }
    if (!r.ok) throw new Error("تعذر تحميل اتفاقات الخاص."); setData(await r.json()); setHidden(false);
  }, [path]);
  useEffect(() => { const controller = new AbortController(); void Promise.resolve().then(() => load(controller.signal)).catch(e => { if (!controller.signal.aborted) setError(e.message); }); return () => controller.abort(); }, [load]);
  async function save(e: FormEvent<HTMLFormElement>) {
    e.preventDefault(); if (busy) return; const f = new FormData(e.currentTarget);
    const body = { branchId: f.get("branch"), sportId: f.get("sport"), effectiveFrom: f.get("from"), effectiveTo: f.get("to") || null, variableMethod: method, variableValue: Number(f.get("value")), eligibility: f.get("eligibility"), baseCoverage: f.get("coverage"), note: f.get("note") };
    const signature = JSON.stringify(body); if (!keys.current.has(signature)) keys.current.set(signature, crypto.randomUUID());
    setBusy(true); setError(""); setMessage("");
    try { await csrfRequest(path, "POST", body, { "Idempotency-Key": keys.current.get(signature)! }); setFormOpen(false); keys.current.clear(); await load(); setMessage("تم حفظ مسودة الاتفاق؛ لا تصبح نافذة قبل الاعتماد."); }
    catch (e) { setError(e instanceof Error ? e.message : "تعذر حفظ الاتفاق."); } finally { setBusy(false); }
  }
  async function approve() {
    if (!selected || busy || !data) return; setBusy(true); setError("");
    const latest = data.items.filter(a => a.version && a.branchId === selected.branchId && a.sportId === selected.sportId).sort((a, b) => b.version! - a.version!)[0];
    const expected = data.latestApproved ? data.latestApproved.find(a => a.branchId === selected.branchId && a.sportId === selected.sportId)?.agreementId : latest?.id;
    try { await csrfRequest(`${path}/${selected.id}/approve`, "POST", { expectedLatestAgreementId: expected ?? null }); setSelected(null); await load(); setMessage("تم اعتماد نسخة الاتفاق وحفظ شروطها دون تغيير النسخ السابقة."); }
    catch (e) { setSelected(null); setError(e instanceof Error ? e.message : "تعذر اعتماد الاتفاق."); } finally { setBusy(false); }
  }
  if (hidden) return null;
  return <section className="detail-card coach-agreements" aria-label="اتفاقات أجر الخاص"><div className="section-heading"><div><h2>اتفاقات أجر الخاص</h2><p className="muted">نسخ مؤرخة ومعتمدة، لا نسبة افتراضية مخفية ولا تعديل رجعي.</p></div>{canManage && data?.configured && <button className="primary-button" onClick={() => { setFormOpen(true); setMessage(""); }}>نسخة اتفاق جديدة</button>}</div>
    {error && <ErrorState message={error} />}{message && <p role="status">{message}</p>}{!data && !error && <LoadingState />}
    {data && !data.configured ? <p>اربط المدرب بسجل موظف أولًا من <Link href="/dashboard/staff/employees/new">الموظفين</Link>، دون إنشاء حساب دخول ثانٍ.</p> : data && <>
      <p>الأساسي الحالي: <strong>{formatMoneyAr(data.monthlyBase, data.currency)}</strong> <Link href={`/dashboard/payroll?employeeId=${data.employeeId}`}>فتح المستحقات</Link></p>
      <p className="muted">الأساسي مصدره الرواتب الحالية، وليس راتبًا إضافيًا من الاتفاق. اعتماد الشروط لا يثبت تنفيذ حصة أو تحصيلًا أو عمولة، ولا يجري صرفًا.</p>
      {formOpen && canManage && <form className="form-shell" onSubmit={e => void save(e)} aria-label="نسخة اتفاق الخاص">
        <label>فرع الاتفاق<select name="branch" required defaultValue=""><option value="">اختر الفرع</option>{data.branches.map(b => <option key={b.id} value={b.id}>{b.name}</option>)}</select></label>
        <label>رياضة الاتفاق<select name="sport" required defaultValue=""><option value="">اختر الرياضة</option>{data.sports.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}</select></label>
        <label>ساري من<input name="from" type="date" required min={data.today} max={data.contractEnd} defaultValue={data.today} /></label><label>ساري إلى<input name="to" type="date" min={data.today} max={data.contractEnd} required={Boolean(data.contractEnd)} defaultValue={data.contractEnd ?? ""} /></label>
        <label>أسلوب أجر الخاص<select name="method" value={method} onChange={e => setMethod(e.target.value)}><option value="Percent">نسبة من المقابل المؤهل</option><option value="PerSession">مبلغ لكل حصة مؤهلة</option></select></label>
        <label>{method === "Percent" ? "النسبة المئوية" : "مبلغ الحصة"}<input name="value" type="number" required min="0" max={method === "Percent" ? "100" : "100000000"} step="0.01" placeholder="أدخل قيمة، حتى لو صفرًا" /></label>
        <label>شرط الاستحقاق<select name="eligibility" defaultValue="DeliveredCollected"><option value="DeliveredCollected">تنفيذ معتمد وتحصيل صافٍ</option><option value="Delivered">تنفيذ معتمد حتى لو لم يحصل</option></select></label>
        <label>تغطية الأساسي<select name="coverage" defaultValue="GroupOnly"><option value="GroupOnly">الأساسي للجماعي، والخاص إضافي</option><option value="IncludesPrivate">الخاص مشمول بالأساسي (متغير صفر)</option></select></label>
        <label>ملاحظة الاتفاق (اختياري)<textarea name="note" maxLength={500} /></label><p className="muted">التسوية شهرية. النسبة على الصافي بعد الخصومات والمرتجعات، دون ضريبة وقبل رسوم الدفع؛ التحصيل الجزئي يوزع بالتناسب. الحضور الوظيفي وحده لا ينشئ أجر الخاص.</p>
        <div className="form-actions"><button className="primary-button" disabled={busy}>حفظ مسودة الاتفاق</button><button type="button" className="secondary-button" disabled={busy} onClick={() => setFormOpen(false)}>إلغاء</button></div>
      </form>}
      <div className="linked-list">{!data.items.length ? <p>لا اتفاقات مسجلة. لا تُفترض نسبة للمدرب.</p> : data.items.map(a => <article key={a.id}><div><strong>{a.branch} · {a.sport}</strong><p>{a.variableMethod === "Percent" ? `${a.variableValue}%` : `${formatMoneyAr(a.variableValue, a.currency)} لكل حصة`} · {a.eligibility === "DeliveredCollected" ? "منفذ ومحصّل" : "منفذ ولو لم يحصل"}</p><small>{formatDateAr(a.effectiveFrom)} — {a.effectiveTo ? formatDateAr(a.effectiveTo) : "دون نهاية محددة"} · {a.version ? `معتمد — نسخة ${a.version}` : "مسودة غير نافذة"}</small>{a.approvedBy && <p className="muted">اعتمد بواسطة {a.approvedBy}{a.approvedAtUtc ? ` · ${new Intl.DateTimeFormat("ar-EG-u-nu-latn", { timeZone: data.timeZone ?? "Africa/Cairo", dateStyle: "medium", timeStyle: "short" }).format(new Date(a.approvedAtUtc))}` : ""}</p>}{a.note && <p>{a.note}</p>}</div>{!a.version && canManage && <button className="primary-action" disabled={busy} onClick={() => setSelected(a)}>مراجعة واعتماد</button>}</article>)}</div>
    </>}
    <ConfirmationDialog open={selected !== null} title="اعتماد شروط أجر الخاص؟" message={selected ? `${selected.branch} · ${selected.sport} · ${selected.variableValue}${selected.variableMethod === "Percent" ? "%" : ` ${selected.currency} لكل حصة`} · سريان ${formatDateAr(selected.effectiveFrom)}. تبقى النسخ السابقة ثابتة؛ لا صرف أو عمولة عند الاعتماد.` : ""} confirmLabel="تأكيد اعتماد الاتفاق" busy={busy} onConfirm={() => void approve()} onCancel={() => setSelected(null)} />
  </section>;
}
