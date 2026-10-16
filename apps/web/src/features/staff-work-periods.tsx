"use client";
import { FormEvent, useEffect, useState } from "react";
import { TimeField } from "../components/time-field";
import { ErrorState, LoadingState } from "../components/dashboard-shell";
import { formatDateAr, formatTimeAr, formatStaffCheckout } from "../lib/formatters";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";

export type WorkPeriod = { id: string | null; version: number; startDate: string; startTime: string; endDate?: string | null; endTime?: string | null; timeZoneId?: string | null; trustSource: string; workedSeconds?: number | null };
type PeriodDay = { version: number; today: string; workDate: string; timeZone: string; hasPreservedPeriods: boolean; canEdit: boolean; items: WorkPeriod[]; knownWorkedSeconds: number; totalWorkedSeconds: number | null };
export function PeriodSummary({ value }: { value: WorkPeriod }) {
  return <><span>{formatDateAr(value.startDate)} · {formatTimeAr(value.startTime)} — {value.endTime ? formatStaffCheckout(value.endTime, value.endDate ?? undefined, value.startDate) : "فترة مفتوحة — بلا انصراف مختلق"}</span><small>{value.trustSource === "LegacyLocal" ? "سجل قديم محلي — مدته غير مؤكدة" : value.trustSource === "ServerVerified" ? "بداية بتوقيع متحقق خادميًا — ليست ضمانًا ضد تزوير الموقع" : value.trustSource === "MixedVerifiedEnd" ? "بداية إدارية وانصراف بتوقيع متحقق خادميًا" : "توقيت معلن إداريًا — ليس إثبات جهاز"}{value.timeZoneId && (value.timeZoneId === "Africa/Cairo" ? " · توقيت القاهرة المحفوظ" : " · منطقة التوقيت محفوظة مع الفترة")}{value.workedSeconds != null && ` · ${Math.floor(value.workedSeconds / 60)} دقيقة`}</small></>;
}
export function StaffWorkPeriodsPanel({ id, date, onSaved }: { id: string; date: string; onSaved: () => void }) {
  const [data, setData] = useState<PeriodDay | null>(null), [error, setError] = useState("");
  const [editor, setEditor] = useState<WorkPeriod | "new" | null>(null), [busy, setBusy] = useState(false), [stale, setStale] = useState(false);
  const [command, setCommand] = useState<{ path: string; method: "PUT" | "POST"; body: object; key: string } | null>(null);
  useEffect(() => {
    const abort = new AbortController();
    fetch(`/api/v1/manage/staff-attendance/${id}/periods?date=${date}`, { signal: abort.signal }).then(async r => {
      if (!r.ok) throw new Error(); setData(await r.json()); setError("");
    }).catch(e => { if (e.name !== "AbortError") setError("تعذر تحميل فترات العمل."); });
    return () => abort.abort();
  }, [id, date]);
  async function save(e: FormEvent<HTMLFormElement>) {
    e.preventDefault(); if (!data || !editor || busy || stale || !data.canEdit) return;
    const f = new FormData(e.currentTarget), selected = editor === "new" ? null : editor;
    const c = command ?? { path: `/api/v1/manage/staff-attendance/${id}/periods${selected ? `/${selected.id}` : ""}`, method: selected ? "PUT" as const : "POST" as const, key: crypto.randomUUID(), body: { date, version: data.version, startTime: `${f.get("start")}:00`, endTime: f.get("end") ? `${f.get("end")}:00` : null, endDate: f.get("end") ? f.get("endDate") || date : null, periodVersion: selected?.version ?? null, reason: f.get("reason") || null } };
    setBusy(true); setCommand(c); setError("");
    try { await csrfRequest(c.path, c.method, c.body, { "Idempotency-Key": c.key }, { announce: false }); window.dispatchEvent(new CustomEvent("academy-save-success", { detail: "تم حفظ فترة العمل دون تكرار خصم اليوم." })); onSaved(); }
    catch (e) { if (e instanceof DashboardRequestError) { setCommand(null); if (e.status === 409) setStale(true); } setError(e instanceof DashboardRequestError ? e.message : "تعذر تأكيد الحفظ؛ أعد إرسال نفس الفترة دون تغيير البيانات."); }
    finally { setBusy(false); }
  }
  return <section className="staff-periods" aria-label="فترات العمل"><h2>فترات العمل</h2><p>كل فترة مستقلة؛ الفاصل بين الفترات ليس وقت عمل. خصم اليوم يبقى مرة واحدة.</p>
    {error && <ErrorState message={error} />}{!data && !error ? <LoadingState /> : data && <>
      <p>الفترات بتوقيتها المحفوظ. إدخال التوقيت حسب إعداد الأكاديمية الحالي.</p>
      {data.totalWorkedSeconds != null ? <p>إجمالي الفترات: {Math.floor(data.totalWorkedSeconds / 60)} دقيقة</p> : data.items.length > 0 && <p>الإجمالي غير مكتمل أو غير مؤكد · الفترات المعلومة: {Math.floor(data.knownWorkedSeconds / 60)} دقيقة</p>}
      {!data.items.length ? <p>سجل الحضور الأول من النموذج أعلاه، ثم أضف الفترات التالية.</p> : <ol className="staff-period-list">{data.items.map((p, i) => <li key={p.id ?? "original"}><strong>الفترة {i + 1}</strong><PeriodSummary value={p} />{data.canEdit && p.id && <button type="button" disabled={busy || !!command || stale} onClick={() => setEditor(p)}>تصحيح / إغلاق الفترة {i + 1}</button>}</li>)}</ol>}
      {!editor && data.canEdit && data.items.length > 0 && <button type="button" disabled={data.items.some(p => !p.endTime)} onClick={() => setEditor("new")}>إضافة فترة عمل</button>}
      {data.items.some(p => !p.endTime) && <p>هناك فترة مفتوحة؛ أغلقها من مصدرها قبل فتح فترة أخرى.</p>}
      {editor && <form aria-label="حفظ فترة العمل" onSubmit={e => void save(e)}>
        <fieldset className="form-shell staff-daily-edit-fields" disabled={busy || !!command || stale} key={editor === "new" ? "new" : editor.id}>
          <legend>{editor === "new" ? "فترة إضافية" : "تصحيح الفترة المحددة"}</legend><p>يوم بدء العمل: {formatDateAr(date)}</p>
          <TimeField label="بداية الفترة" name="start" required defaultValue={editor === "new" ? "" : editor.startTime} />
          <TimeField label="نهاية الفترة" name="end" defaultValue={editor === "new" ? "" : editor.endTime ?? ""} />
          <label>تاريخ نهاية الفترة<input aria-label="تاريخ نهاية الفترة" name="endDate" type="date" min={date} max={data.today} defaultValue={editor === "new" ? date : editor.endDate ?? date} /><small>بدون وقت نهاية تبقى الفترة مفتوحة. اختر اليوم التالي للعمل الليلي.</small></label>
          <label>سبب التصحيح{editor === "new" ? " (اختياري)" : " (مطلوب)"}<textarea name="reason" required={editor !== "new"} minLength={editor === "new" ? undefined : 5} maxLength={500} /></label>
        </fieldset><div className="form-actions"><button className="primary-button" disabled={busy || stale}>{busy ? "جارٍ حفظ الفترة…" : command ? "إعادة إرسال نفس الفترة" : "حفظ الفترة"}</button>{!command && !busy && !stale && <button type="button" onClick={() => setEditor(null)}>إلغاء</button>}</div>
      </form>}
      {stale && <button type="button" onClick={onSaved}>إعادة تحميل اليوم والفترات</button>}
    </>}
  </section>;
}
