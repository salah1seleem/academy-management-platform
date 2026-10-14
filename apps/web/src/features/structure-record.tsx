"use client";

import Link from "next/link";
import { FormEvent, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { ConfirmationDialog, ErrorState, LoadingState, PageHeader, RowActions } from "../components/dashboard-shell";
import { csrfRequest } from "./dashboard-api";
import { GroupWorkspace } from "./group-workspace";

export type StructureKind = "branches" | "sports" | "categories" | "groups" | "coaches";
type RecordData = { id: string; arabicName: string; englishName?: string; description?: string; isActive: boolean; minimumBirthYear?: number; maximumBirthYear?: number; branchId?: string; sportId?: string; ageCategoryId?: string; capacity?: number; branchName?: string; sportName?: string; categoryName?: string; assignments?: Assignment[] };
type Assignment = { id: string; groupName: string; isActive: boolean };
type Option = { id: string; arabicName: string };
type Options = { branches: Option[]; sports: Option[]; categories: Option[] };
type CoachPerformance = { from: string; to: string; groups: { id: string; name: string; branch: string; sessions: number }[]; sessionCount: number; publishedEvaluations: number; parentRating: number | null; parentRatingCount: number; complaintCount: number };
const titles: Record<StructureKind, string> = { branches: "الفرع", sports: "الرياضة", categories: "الفئة العمرية", groups: "المجموعة", coaches: "المدرب" };

export function StructureDetails({ kind, id, performanceFilters = {} }: { kind: StructureKind; id: string; performanceFilters?: { from?: string; to?: string; branchId?: string; allDates?: string } }) {
  const [record, setRecord] = useState<RecordData | null>(null);
  const [error, setError] = useState("");
  const [target, setTarget] = useState<Assignment | null>(null);
  useEffect(() => { fetch(`/api/v1/manage/structure/${kind}/${id}`).then(async response => { if (!response.ok) throw new Error(); setRecord(await response.json() as RecordData); }).catch(() => setError("تعذر تحميل السجل أو أنه خارج نطاق الأكاديمية.")); }, [id, kind]);
  async function changeAssignment() { if (!target) return; try { await csrfRequest(`/api/v1/manage/group-assignments/${target.id}/status`, "PUT", { isActive: !target.isActive }); const response = await fetch(`/api/v1/manage/structure/${kind}/${id}`); if (!response.ok) throw new Error("تعذر تحديث البيانات."); setRecord(await response.json() as RecordData); setTarget(null); } catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر تحديث الإسناد."); } }
  return <><PageHeader title={`عرض ${titles[kind]}`} context={`الأكاديمية / ${titles[kind]}`} action={kind === "coaches" ? { label: "إسناد مجموعة", href: `/dashboard/academy/coaches/new?coachId=${id}` } : { label: "تعديل", href: `/dashboard/academy/${kind}/${id}/edit` }} />{error ? <ErrorState message={error} /> : !record ? <LoadingState /> : <section className="detail-card"><h2>{record.arabicName}</h2><dl className="detail-grid"><div><dt>الحالة</dt><dd>{record.isActive ? "فعال" : "متوقف"}</dd></div>{record.englishName && <div><dt>الاسم بالإنجليزية</dt><dd dir="ltr">{record.englishName}</dd></div>}{record.description && <div><dt>الوصف/العنوان</dt><dd>{record.description}</dd></div>}{record.branchName && <div><dt>الفرع</dt><dd>{record.branchName}</dd></div>}{record.sportName && <div><dt>الرياضة</dt><dd>{record.sportName}</dd></div>}{record.categoryName && <div><dt>الفئة العمرية</dt><dd>{record.categoryName}</dd></div>}</dl>{kind === "coaches" && <div className="linked-list"><h3>المجموعات المسندة</h3>{record.assignments?.length ? record.assignments.map(assignment => <article key={assignment.id} className={!assignment.isActive ? "inactive-record" : ""}><strong>{assignment.groupName}</strong><RowActions actions={[{ label: assignment.isActive ? "إزالة الإسناد" : "إعادة الإسناد", onClick: () => setTarget(assignment), tone: assignment.isActive ? "danger" : "normal" }]} /></article>) : <p>لا توجد مجموعات مسندة.</p>}</div>}<Link className="back-link" href={`/dashboard/academy/${kind}`}>العودة إلى القائمة</Link></section>}{kind === "coaches" && record && <CoachPerformanceDetails id={id} filters={performanceFilters} />}{kind === "branches" && record && <BranchCheckInSite id={id} />}{kind === "groups" && record && <GroupWorkspace id={id} />}<ConfirmationDialog open={target !== null} title="تأكيد تحديث الإسناد" message={`هل تريد ${target?.isActive ? "إزالة" : "إعادة"} إسناد «${target?.groupName ?? ""}»؟`} confirmLabel="تأكيد" onConfirm={() => void changeAssignment()} onCancel={() => setTarget(null)} /></>;
}

function CoachPerformanceDetails({ id, filters }: { id: string; filters: { from?: string; to?: string; branchId?: string; allDates?: string } }) {
  const [data, setData] = useState<CoachPerformance | null>(null);
  const [visible, setVisible] = useState(true);
  const query = new URLSearchParams(Object.entries(filters).filter(([key, value]) => ["from", "to", "branchId", "allDates"].includes(key) && value) as [string, string][]).toString();
  useEffect(() => { const controller = new AbortController(); fetch(`/api/v1/reports/coach-performance/${id}?${query}`, { signal: controller.signal }).then(r => { if (r.status === 403) { setVisible(false); return null; } if (!r.ok) throw new Error(); return r.json(); }).then(x => { if (x) setData(x); }).catch(() => { if (!controller.signal.aborted) setVisible(false); }); return () => controller.abort(); }, [id, query]);
  if (!visible) return null;
  if (!data) return <LoadingState />;
  return <section className="detail-card" aria-label="أداء المدرب"><h2>أداء المدرب خلال الفترة</h2><p className="muted">من {data.from} إلى {data.to} · الحصص للمجموعات المسندة، والتقييمات المنشورة لهذا المدرب فقط.</p><dl className="detail-grid"><div><dt>حصص المجموعات</dt><dd>{data.sessionCount}</dd></div><div><dt>تقييمات اللاعبين المنشورة</dt><dd>{data.publishedEvaluations}</dd></div><div><dt>تقييم الأسر</dt><dd>{data.parentRating == null ? "لم تُرسل تقييمات" : `${data.parentRating}/5 من ${data.parentRatingCount} تقييم`}</dd></div><div><dt>شكاوى الأسر</dt><dd>{data.complaintCount}</dd></div></dl><div className="linked-list"><h3>الحصص حسب المجموعة</h3>{data.groups.length ? data.groups.map(g => <article key={g.id}><Link href={`/dashboard/academy/groups/${g.id}`}>{g.name}</Link><span>{g.branch} · {g.sessions} حصة</span></article>) : <p>لا توجد مجموعات مسندة في النطاق المختار.</p>}</div><Link href={`/dashboard/feedback?coachId=${id}`}>ملاحظات الأسر المرتبطة بالمدرب</Link></section>;
}

function BranchCheckInSite({ id }: { id: string }) {
  const [site, setSite] = useState<{ checkInLatitude: number | null; checkInLongitude: number | null; history: { createdAtUtc: string; afterLatitude: number | null; afterLongitude: number | null }[] } | null>(null);
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  useEffect(() => { fetch(`/api/v1/manage/branches/${id}/check-in-site`).then(r => r.ok ? r.json() : null).then(setSite).catch(() => setSite(null)); }, [id]);
  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (busy) return;
    const form = event.currentTarget; const values = new FormData(form); const latitude = values.get("latitude")?.toString().trim() ?? ""; const longitude = values.get("longitude")?.toString().trim() ?? "";
    setBusy(true); setMessage("");
    try { await csrfRequest(`/api/v1/manage/branches/${id}/check-in-site`, "PUT", { latitude: latitude ? Number(latitude) : null, longitude: longitude ? Number(longitude) : null }); const refreshed = await fetch(`/api/v1/manage/branches/${id}/check-in-site`); if (!refreshed.ok) throw new Error("تم الحفظ، لكن تعذر تحديث سجل الموقع. أعد تحميل الصفحة."); setSite(await refreshed.json()); setMessage("تم تحديث نقطة حضور الفرع."); }
    catch (caught) { setMessage(caught instanceof Error ? caught.message : "تعذر حفظ موقع الفرع."); }
    finally { setBusy(false); }
  }
  if (!site) return null;
  return <section className="detail-card"><h2>نقطة حضور المدربين</h2><p>للمالك فقط: أدخل إحداثيات موقع الفرع بعد التحقق منها. نطاق الحضور 50 مترًا، ولن يعمل قبل ضبط الموقع. لا تُحفظ إحداثيات المدرب نفسه. التسجيل الذاتي متاح في Demo فقط حتى اكتمال بوابة الأمان.</p><form className="form-shell" onSubmit={event => void save(event)}><label>خط العرض<input name="latitude" type="number" step="any" min="-90" max="90" defaultValue={site.checkInLatitude ?? ""} /></label><label>خط الطول<input name="longitude" type="number" step="any" min="-180" max="180" defaultValue={site.checkInLongitude ?? ""} /></label><button className="primary-button" disabled={busy}>حفظ نقطة الحضور</button></form>{message && <p role="status">{message}</p>}{site.history?.length > 0 && <div className="linked-list"><h3>آخر تغييرات نقطة الحضور</h3>{site.history.slice(0, 3).map(x => <article key={x.createdAtUtc}><span>{new Date(x.createdAtUtc).toLocaleString("ar-EG-u-nu-latn")}</span><strong>{x.afterLatitude == null ? "إيقاف" : `${x.afterLatitude}, ${x.afterLongitude}`}</strong></article>)}</div>}</section>;
}

export function StructureEdit({ kind, id }: { kind: Exclude<StructureKind, "coaches">; id: string }) {
  const router = useRouter();
  const [record, setRecord] = useState<RecordData | null>(null);
  const [options, setOptions] = useState<Options>({ branches: [], sports: [], categories: [] });
  const [error, setError] = useState("");
  useEffect(() => { Promise.all([fetch(`/api/v1/manage/structure/${kind}/${id}`), fetch("/api/v1/manage/structure/options")]).then(async ([recordResponse, optionsResponse]) => { if (!recordResponse.ok || !optionsResponse.ok) throw new Error(); setRecord(await recordResponse.json() as RecordData); setOptions(await optionsResponse.json() as Options); }).catch(() => setError("تعذر تحميل بيانات التعديل.")); }, [id, kind]);
  async function submit(event: FormEvent<HTMLFormElement>) { event.preventDefault(); const data = new FormData(event.currentTarget); try { await csrfRequest(`/api/v1/manage/structure/${kind}/${id}`, "PUT", { arabicName: data.get("arabicName"), englishName: data.get("englishName"), description: data.get("description"), minimumBirthYear: Number(data.get("minimumBirthYear")) || null, maximumBirthYear: Number(data.get("maximumBirthYear")) || null, branchId: data.get("branchId") || null, sportId: data.get("sportId") || null, ageCategoryId: data.get("ageCategoryId") || null, capacity: Number(data.get("capacity")) || null }); router.push(`/dashboard/academy/${kind}`); } catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر حفظ التعديل."); } }
  if (!record) return <><PageHeader title={`تعديل ${titles[kind]}`} context="الأكاديمية / تعديل" />{error ? <ErrorState message={error} /> : <LoadingState />}</>;
  return <><PageHeader title={`تعديل ${titles[kind]}`} context={`الأكاديمية / ${titles[kind]} / تعديل`} /><form className="form-shell" onSubmit={event => void submit(event)}><label>الاسم بالعربية<input name="arabicName" defaultValue={record.arabicName} required /></label><label>الاسم بالإنجليزية<input name="englishName" dir="ltr" defaultValue={record.englishName ?? ""} /></label>{kind === "branches" && <label>العنوان المختصر<input name="description" defaultValue={record.description ?? ""} /></label>}{kind === "categories" && <><label>أقل سنة ميلاد<input name="minimumBirthYear" type="number" defaultValue={record.minimumBirthYear ?? ""} /></label><label>أعلى سنة ميلاد<input name="maximumBirthYear" type="number" defaultValue={record.maximumBirthYear ?? ""} /></label></>}{kind === "groups" && <><label>الفرع<select name="branchId" defaultValue={record.branchId} required>{options.branches.map(option => <option key={option.id} value={option.id}>{option.arabicName}</option>)}</select></label><label>الرياضة<select name="sportId" defaultValue={record.sportId} required>{options.sports.map(option => <option key={option.id} value={option.id}>{option.arabicName}</option>)}</select></label><label>الفئة العمرية<select name="ageCategoryId" defaultValue={record.ageCategoryId} required>{options.categories.map(option => <option key={option.id} value={option.id}>{option.arabicName}</option>)}</select></label><label>السعة<input name="capacity" type="number" min="1" defaultValue={record.capacity ?? ""} /></label></>}{error && <ErrorState message={error} />}<div className="form-actions"><button className="primary-button">حفظ التعديلات</button><button type="button" onClick={() => router.back()}>إلغاء</button></div></form></>;
}
