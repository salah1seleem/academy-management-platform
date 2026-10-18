"use client";
import Link from "next/link";
import { useEffect, useRef, useState, type FormEvent } from "react";
import { ClipboardCheck, CalendarClock, ArrowRight } from "lucide-react";
import { csrfRequest } from "./dashboard-api";
import { conditions, followUpStates, DraftReview, type DraftReviewData } from "./owner-followups";
import "./assigned-followups.css";
type Item = { id: string; version: number; state: string; condition: string; conditionActive: boolean | null; overdue: boolean; dueOn: string | null; note: string | null; resolutionReason: string | null; sourceUrl: string; source: { name: string; branch: string; sport: string; enrollments: number; capacity: number | null; evaluationDrafts?: DraftReviewData | null; coverageGaps?: { sessionDate: string; startTime: string }[] } | null };
type Data = { item?: Item; items?: Item[]; history?: { id: string; createdAtUtc: string; after: string }[]; canEdit: boolean; canOpenSource: boolean; hasMore?: boolean };
export function AssignedFollowUps({ id }: { id?: string }) {
  const [range, setRange] = useState<{ from?: string; to?: string; allDates?: string }>({});
  const [state, setState] = useState("Open"), [revision, setRevision] = useState(0);
  const [notice, setNotice] = useState("");
  const key = JSON.stringify([id, range, state, revision]);
  const [result, setResult] = useState<{ key: string; data?: Data; error?: string }>();
  const data = result?.key === key ? result.data : undefined, error = result?.key === key ? result.error : undefined;
  useEffect(() => {
    const c = new AbortController();
    const path = id ? `/api/v1/my-followups/${id}` : `/api/v1/my-followups?${new URLSearchParams({ ...range, state })}`;
    void fetch(path, { cache: "no-store", signal: c.signal }).then(async r => {
      if (!r.ok) throw new Error(r.status === 403 || r.status === 404 ? "المتابعة غير متاحة ضمن إسنادك وصلاحياتك الحالية." : "تعذر تحميل المتابعات. أعد المحاولة.");
      const value = await r.json(); if (!c.signal.aborted) { setResult({ key, data: value }); if (!id && !range.allDates && !range.from && !range.to) setRange({ from: value.from, to: value.to }); }
    }).catch(e => { if (!c.signal.aborted) setResult({ key, error: e.message }); });
    return () => c.abort();
  }, [key, id, range, state]);
  useEffect(() => { const refresh = () => setRevision(x => x + 1); window.addEventListener("focus", refresh); return () => window.removeEventListener("focus", refresh); }, []);
  return <section className="assigned-followups" dir="rtl"><header><span><ClipboardCheck/>التشغيل اليومي</span><h1>{id ? "تفاصيل المتابعة المسندة" : "متابعاتي المسندة"}</h1><p>مهامك في نطاق فرعك ورياضتك. تحديثها لا يغيّر الحضور أو الإسناد أو أي مبلغ.</p></header>
    {id ? <Link href="/dashboard/my-followups"><ArrowRight/>العودة لمتابعاتي</Link> : <div className="assigned-filters"><label>من تاريخ<input type="date" value={range.from ?? ""} onChange={e => setRange(r => ({ ...r, from: e.target.value, allDates: "true" }))}/></label><label>إلى تاريخ<input type="date" value={range.to ?? ""} onChange={e => setRange(r => ({ ...r, to: e.target.value, allDates: "true" }))}/></label><label>الحالة<select value={state} onChange={e => setState(e.target.value)}><option value="">كل الحالات</option><option value="Open">المفتوحة</option>{Object.entries(followUpStates).map(([s, label]) => <option key={s} value={s}>{label}</option>)}</select></label><button className="secondary-button" onClick={() => { setRange({ allDates: "true" }); setState(""); }}>مسح</button></div>}
    {notice && <p role="status">{notice}</p>}{error ? <p role="alert">{error}</p> : !data ? <p role="status">جارٍ تحميل متابعاتك…</p> : id && data.item ? <><Summary item={data.item} canOpenSource={data.canOpenSource}/>{data.canEdit ? <Update key={`${data.item.id}:${data.item.version}`} item={data.item} saved={() => { setNotice("تم تحديث المتابعة وتسجيل الإجراء."); setRevision(x => x + 1); }}/> : <p role="status">للقراءة فقط أو متابعة مغلقة. الإسناد وإعادة الفتح لدى المالك.</p>}<h2>سجل المتابعة</h2>{data.history?.map(h => { const after = JSON.parse(h.after); return <article key={h.id}><strong>{followUpStates[after.State]}</strong><time>{new Date(h.createdAtUtc).toLocaleString("ar-EG-u-nu-latn")}</time><p>{after.Note}</p>{after.ResolutionReason && <p>السبب: {after.ResolutionReason}</p>}</article>; })}</> : <><div className="assigned-grid">{data.items?.map(i => <article key={i.id}><Summary item={i} canOpenSource={false}/><Link href={`/dashboard/my-followups/${i.id}`}>فتح المتابعة</Link></article>)}</div>{!data.items?.length && <p role="status">لا توجد متابعات مسندة إليك ضمن الفلتر.</p>}{data.hasMore && <p>تظهر أول 100 متابعة؛ ضيق الفترة لعرض الباقي.</p>}</>}
    <button className="secondary-button" onClick={() => setRevision(x => x + 1)}>تحديث المتابعات</button>
  </section>;
}
function Summary({ item: i, canOpenSource }: { item: Item; canOpenSource: boolean }) {
  return <div className="assigned-summary"><span className="assigned-status">{followUpStates[i.state]}{i.overdue ? " · متأخرة" : ""}</span>
    <h2>{i.source?.name ?? "المصدر غير متاح"}</h2><p>{conditions[i.condition]}</p><p>{i.source?.branch} · {i.source?.sport}</p>
    {i.condition === "GroupEvaluationDrafts" && <DraftReview data={i.source?.evaluationDrafts}/>}
    {i.condition === "GroupLowOccupancy" && i.source && <p>تسجيلات فعالة مسندة: {i.source.enrollments}، السعة: {i.source.capacity ?? "غير محددة"}</p>}
    {i.condition === "GroupSessionCoverage" && <p>{i.source?.coverageGaps?.length ?? 0} حصة قادمة بلا إسناد مدرب مخول؛ ليست غيابًا مسجلًا أو خصمًا.</p>}
    <p><CalendarClock size={18}/>موعد المتابعة: <bdi>{i.dueOn ?? "غير محدد"}</bdi></p>
    <p>الحالة الأصلية: {i.conditionActive === true ? "ما زالت قائمة؛ إغلاق المتابعة لا يحلها تلقائيًا" : i.conditionActive === false ? "لم تعد تنطبق أو المصدر متوقف" : "غير متاحة، لا نفترض أنها حُلت"}</p>
    {i.source?.coverageGaps?.slice(0, 3).map((s, n) => <p key={n}><bdi>{s.sessionDate} · {s.startTime.slice(0, 5)}</bdi></p>)}{i.note && <p>{i.note}</p>}{i.resolutionReason && <p>سبب الإغلاق: {i.resolutionReason}</p>}{canOpenSource && <Link href={i.sourceUrl}>فتح المجموعة نفسها</Link>}</div>;
}
function Update({ item, saved }: { item: Item; saved: () => void }) {
  const [state, setState] = useState("InProgress"), [due, setDue] = useState(item.dueOn ?? ""), [note, setNote] = useState(item.note ?? ""), [reason, setReason] = useState(""), [busy, setBusy] = useState(false), [message, setMessage] = useState("");
  const attempt = useRef<{ draft: string; id: string } | null>(null);
  async function submit(e: FormEvent) { e.preventDefault(); if (busy) return; const body = { state, version: item.version, dueOn: due || null, note, resolutionReason: reason || null }, draft = JSON.stringify(body); if (attempt.current?.draft !== draft) attempt.current = { draft, id: crypto.randomUUID() }; setBusy(true); setMessage(""); try { await csrfRequest(`/api/v1/my-followups/${item.id}`, "POST", { ...body, requestId: attempt.current.id }); setMessage("تم تحديث المتابعة وتسجيل الإجراء."); saved(); } catch (e) { setMessage(e instanceof Error ? e.message : "تعذر الحفظ."); } finally { setBusy(false); } }
  return <form className="assigned-update" onSubmit={submit}><h2>تحديث عملي</h2><label>حالة المتابعة<select value={state} onChange={e => setState(e.target.value)} disabled={busy}>{["InProgress", "Snoozed", "Closed", "Rejected"].map(s => <option key={s} value={s}>{followUpStates[s]}</option>)}</select></label><label>موعد المتابعة<input type="date" value={due} required={state === "Snoozed"} disabled={busy} onChange={e => setDue(e.target.value)}/></label><label>ملاحظة اختيارية<textarea maxLength={500} value={note} disabled={busy} onChange={e => setNote(e.target.value)}/></label>{["Closed", "Rejected"].includes(state) && <label>سبب إغلاق أو رفض المتابعة<textarea required maxLength={500} value={reason} disabled={busy} onChange={e => setReason(e.target.value)}/></label>}<button disabled={busy}>{busy ? "جارٍ الحفظ…" : "حفظ تحديث المتابعة"}</button>{message && <p role="status">{message}</p>}</form>;
}
