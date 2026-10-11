"use client";
import { FormEvent, useEffect, useRef, useState } from "react";
import { CalendarCheck2, ShieldCheck, Waves } from "lucide-react";
import { useRouter } from "next/navigation";
import { ErrorState } from "../components/dashboard-shell";
import { TimeField } from "../components/time-field";
import { WeekdaysField } from "../components/weekdays-field";
import { formatDateAr } from "../lib/formatters";
import { announceSaveSuccess, csrfRequest } from "./dashboard-api";
export type ResourcePlan = { resourceId: string; coachMembershipId: string; seats: number; exclusive: boolean };
export type GroupSchedule = { dayOfWeek: number | string; startTime: string; endTime: string; validFrom?: string; validTo?: string };
type Resource = { id: string; name: string; facilitySpaceId: string; branchId: string; capacity: number; approvedAtUtc?: string; serviceKinds: string[] };
type Resources = { resources: Resource[]; spaces: { id: string; name: string; sportId: string }[] };
type Preview = { fingerprint: string; proposedSessions: number; cancelledSessions: number; preservedSessions: number; cancelledBookings: number; expectedPlayers: number; resource?: string; conflicts: { date: string; message: string }[] };
type Props = { id: string; schedules: GroupSchedule[]; group?: { branchId: string; sportId: string }; coaches?: { id: string; name: string }[]; resourcePlan?: ResourcePlan | null; players: number };
export function GroupTimetableForm({ id, schedules, group, coaches = [], resourcePlan, players }: Props) {
  const router = useRouter(), intent = useRef("");
  const [resources, setResources] = useState<Resources | null>(null), [resource, setResource] = useState(resourcePlan?.resourceId ?? "");
  const [remove, setRemove] = useState(false), [preview, setPreview] = useState<Preview | null>(null), [error, setError] = useState(""), [busy, setBusy] = useState(false);
  useEffect(() => {
    const controller = new AbortController();
    void fetch("/api/v1/my-permissions", { signal: controller.signal }).then(async r => {
      if (!r.ok) return;
      const permissions = await r.json();
      if (!Array.isArray(permissions) || !permissions.includes("facilities.read")) return;
      const response = await fetch("/api/v1/facilities", { signal: controller.signal });
      if (response.ok && !controller.signal.aborted) setResources(await response.json());
    }).catch(() => {});
    return () => controller.abort();
  }, []);
  function invalidate() { setPreview(null); intent.current = ""; }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setError("");
    const form = new FormData(event.currentTarget);
    if (!form.getAll("days").length) { setError("اختر يوم تدريب واحدًا على الأقل."); return; }
    const body = { from: form.get("from"), to: form.get("to"), days: form.getAll("days").map(Number), startTime: `${form.get("start")}:00`, endTime: `${form.get("end")}:00`, ...(remove ? { removeResource: true } : resource && resources ? { resource: { resourceId: resource, coachMembershipId: form.get("coach"), seats: Number(form.get("seats")), exclusive: form.get("exclusive") === "on" } } : {}) };
    setBusy(true);
    try {
      if (!preview) {
        const result = await csrfRequest(`/api/v1/manage/groups/${id}/timetable/preview`, "POST", body).then(r => r.json());
        setPreview(result); intent.current ||= crypto.randomUUID();
      } else if (!preview.conflicts.length) {
        await csrfRequest(`/api/v1/manage/groups/${id}/timetable`, "POST", { ...body, previewFingerprint: preview.fingerprint }, { "Idempotency-Key": intent.current }).then(async r => {
          const result = await r.json(); announceSaveSuccess(`تم حفظ الجدول وإنشاء ${result.created} حصة بنجاح.`);
        });
        router.push("/dashboard/academy/groups");
      }
    } catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر حفظ الجدول."); setPreview(null); }
    finally { setBusy(false); }
  }
  const allowed = (resources?.resources ?? []).filter(r => r.approvedAtUtc && r.serviceKinds.includes("Training") && r.branchId === group?.branchId && resources?.spaces.some(s => s.id === r.facilitySpaceId && s.sportId === group?.sportId));
  const selected = resources?.resources.find(r => r.id === resource);
  return <form className="form-shell timetable-form" onSubmit={event => void submit(event)} onChange={invalidate}>
    <WeekdaysField initialDays={schedules.map(s => s.dayOfWeek)} />
    <label>من تاريخ<input type="date" name="from" required defaultValue={schedules[0]?.validFrom} /></label>
    <label>إلى تاريخ<input type="date" name="to" required defaultValue={schedules[0]?.validTo} /></label>
    <label className="timetable-duration">مدة سريعة<select defaultValue="" onChange={e => {
      const form = e.currentTarget.form, from = form?.elements.namedItem("from") as HTMLInputElement | null, to = form?.elements.namedItem("to") as HTMLInputElement | null;
      if (from?.value && to && e.target.value) { const date = new Date(`${from.value}T12:00:00Z`), day = date.getUTCDate(); date.setUTCDate(1); date.setUTCMonth(date.getUTCMonth() + Number(e.target.value)); const lastDay = new Date(Date.UTC(date.getUTCFullYear(), date.getUTCMonth() + 1, 0)).getUTCDate(); date.setUTCDate(Math.min(day, lastDay)); date.setUTCDate(date.getUTCDate() - 1); to.value = date.toISOString().slice(0, 10); }
    }}><option value="">اختر المدة بعد تاريخ البداية</option><option value="1">شهر</option><option value="6">ستة أشهر</option><option value="12">سنة</option></select></label>
    <TimeField label="وقت البداية" name="start" required defaultValue={schedules[0]?.startTime} /><TimeField label="وقت النهاية" name="end" required defaultValue={schedules[0]?.endTime} />
    {(resources || resourcePlan) && <fieldset className="timetable-resource"><legend><Waves size={18} />حجز مساحة التدريب</legend>
      {resources ? <label>الحارة أو منطقة التدريب<select value={resource} disabled={remove} onChange={e => setResource(e.target.value)}><option value="">دون تغيير المورد الحالي</option>{allowed.map(r => <option value={r.id} key={r.id}>{resources.spaces.find(s => s.id === r.facilitySpaceId)?.name} · {r.name} · سعة {r.capacity}</option>)}</select></label> : <p>المورد المحجوز سيبقى مرتبطًا بالجدول؛ تعديله يتطلب صلاحية المرافق.</p>}
      {resource && resources && !remove && <><label>المدرب المسند<select name="coach" required defaultValue={resourcePlan?.coachMembershipId ?? ""}><option value="">اختر مدرب المجموعة</option>{coaches.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}</select></label><label>المقاعد المحجوزة<input name="seats" type="number" min={Math.max(1, players)} max={selected?.capacity ?? 10000} defaultValue={resourcePlan?.seats ?? Math.max(1, players)} required /></label><label className="checkbox-line"><input name="exclusive" type="checkbox" defaultChecked={resourcePlan?.exclusive ?? false} />حجز حصري للمورد</label></>}
      {resourcePlan && resources && <label className="checkbox-line"><input type="checkbox" checked={remove} onChange={e => setRemove(e.target.checked)} />إزالة ربط المورد للحصص المستقبلية في المعاينة</label>}
    </fieldset>}
    {preview && <section className="timetable-preview" aria-label="معاينة أثر الجدول" aria-live="polite"><header><ShieldCheck size={22} /><h3>راجع التغييرات قبل الحفظ</h3></header><div className="timetable-preview-counts"><p><strong>{preview.proposedSessions}</strong>حصة ستنشأ</p><p><strong>{preview.cancelledSessions}</strong>حصة ستلغى</p><p><strong>{preview.preservedSessions}</strong>حصة محفوظة بتاريخها وحضورها</p><p><strong>{preview.cancelledBookings}</strong>حجز سيستبدل</p></div><p>{preview.resource ? `المورد: ${preview.resource}` : "لا حجز مورد جديد"} · {preview.expectedPlayers} لاعب مسند</p><p className="muted">يشمل الاستبدال الحصص المستقبلية من تاريخ البداية، حتى المواعيد بعد نهاية الفترة الجديدة. لا يغير تحصيلًا أو رصيدًا أو حضورًا مسجلًا.</p>{preview.conflicts.length > 0 && <div role="alert"><h4>تعارضات تمنع حفظ الدفعة كلها</h4><ul>{preview.conflicts.map((c, i) => <li key={`${c.date}-${i}`}>{formatDateAr(c.date)} · {c.message}</li>)}</ul></div>}</section>}
    {error && <ErrorState message={error} />}
    <div className="form-actions"><button className="primary-button" disabled={busy || !!preview?.conflicts.length}><CalendarCheck2 size={18} />{busy ? "جارٍ التحقق…" : preview ? "حفظ وإنشاء الحصص" : "معاينة الجدول"}</button>{preview && <button type="button" className="secondary-button" onClick={invalidate} disabled={busy}>إعادة المعاينة</button>}</div>
  </form>;
}
