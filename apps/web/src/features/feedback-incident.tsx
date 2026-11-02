"use client";
import { useEffect, useState } from "react";
import styles from "./feedback-case.module.css";
export type FeedbackIncident = { id: string; groupId: string; groupName: string; date: string; startTime: string; endTime: string; coaches: { id: string; name: string; source: string }[] };
export function FeedbackIncidentPicker({ playerId, day, value, changed }: { playerId: string; day: string; value: FeedbackIncident | null; changed: (v: FeedbackIncident | null) => void }) {
  const [data, setData] = useState<{ sessions: FeedbackIncident[]; notice: string; hasMore: boolean }>(), [error, setError] = useState("");
  useEffect(() => { const controller = new AbortController(); fetch(`/api/v1/guardian/children/${playerId}/feedback-incidents?incidentOn=${encodeURIComponent(day)}`, { signal: controller.signal, cache: "no-store" }).then(async r => { if (!r.ok) throw new Error(); return r.json(); }).then(setData).catch(() => { if (!controller.signal.aborted) setError("تعذر تحميل حصص هذا اليوم؛ يمكنك إرسال ملاحظة عامة دون ربط حصة."); }); return () => controller.abort(); }, [day, playerId]);
  return <div>{error && <p role="alert">{error}</p>}{data && <><label>الحصة المرتبطة (اختياري)<select value={value?.id ?? ""} onChange={e => changed(data.sessions.find(s => s.id === e.target.value) ?? null)}><option value="">لا أعرف / دون حصة</option>{data.sessions.map(s => <option value={s.id} key={s.id}>{s.groupName} · {s.startTime.slice(0, 5)}–{s.endTime.slice(0, 5)}</option>)}</select></label><small>{data.notice}</small>{!data.sessions.length && <p>لا توجد حصة متاحة مرتبطة بطفلك في هذا اليوم.</p>}{data.hasMore && <p>تظهر أول 50 حصة؛ راجع الإدارة لبقية حصص اليوم.</p>}{value && !value.coaches.length && <p>لا يوجد دليل مدرب مؤرخ لهذه الحصة؛ لن نستخدم المدرب الحالي بدلًا منه.</p>}</>}</div>;
}
export function FeedbackIncidentEvidence({ evidence }: { evidence?: string | null }) {
  if (!evidence) return null;
  type Evidence = { groupName: string; date: string; startTime: string; endTime: string; coaches: { id: string; name: string; source: string }[]; selectedCoachId: string | null };
  let s: Evidence | null = null;
  try { const parsed: Evidence = JSON.parse(evidence); if (typeof parsed.groupName === "string" && typeof parsed.date === "string" && typeof parsed.startTime === "string" && typeof parsed.endTime === "string" && Array.isArray(parsed.coaches)) s = parsed; } catch { /* Do not expose a malformed internal payload. */ }
  if (!s) return <p>تعذر عرض سياق الحصة المحفوظ؛ راجع الإدارة.</p>;
  return <section className={styles.incidentEvidence} aria-label="سياق الحصة وقت التقديم"><h3>الحصة المرتبطة وقت تقديم الملاحظة</h3><p>{s.groupName} · {s.date} · <bdi>{s.startTime.slice(0, 5)}–{s.endTime.slice(0, 5)}</bdi></p>{s.coaches.map(c => <p key={c.id}>{c.name} · {c.source === "OccurrenceDecision" ? "قرار مدرب للحصة" : "سجل حضور الحصة"}{s.selectedCoachId === c.id ? " · اختاره مقدم الملاحظة" : ""}</p>)}{!s.selectedCoachId && <p>لم يحدد مقدم الملاحظة مدربًا.</p>}<small>نسخة وقت التقديم من سجلات النظام، وليست إثبات حضور فعلي أو إدانة. لا يعاد بناؤها من إسناد المجموعة الحالي.</small></section>;
}
