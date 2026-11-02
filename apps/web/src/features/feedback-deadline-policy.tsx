export function FeedbackDeadlineEditor({ days, disabled, onChange }: { days?: number | null; disabled: boolean; onChange: (days: number | null) => void }) {
  return <fieldset><legend>مهلة متابعة ملاحظات الأسر الجديدة</legend>
    <p>تبدأ من يوم التقديم بتوقيت الأكاديمية، بأيام تقويمية تشمل العطلات. لا تغيّر الملاحظات القديمة أو توقف العد عند انتظار معلومات. بلاغ السلامة يحتاج مراجعة بشرية عاجلة ولا ينتظر المهلة؛ ليست ضمان حل أو خدمة طوارئ.</p>
    <label><input type="checkbox" checked={days != null} disabled={disabled} onChange={e => onChange(e.target.checked ? 3 : null)}/>تحديد مهلة متابعة تلقائية</label>
    {days != null && <label>مهلة المتابعة بالأيام<input type="number" min={1} max={30} value={days} disabled={disabled} onChange={e => onChange(Number(e.target.value))}/></label>}
    <small>تُحفظ مع مسودة الإعدادات ولا تسري إلا بعد التحقق والاعتماد. الإلغاء يؤثر على التقديم الجديد فقط.</small>
  </fieldset>;
}
export function FeedbackDeadlineEvidence({ evidence }: { evidence?: string | null }) {
  if (!evidence) return <small>موعد يدوي إن حُدد؛ لم تحفظ سياسة تلقائية عند التقديم. لا تظهر هوية مقدم الملاحظة للمدرب موضوعها.</small>;
  const p = readDeadline(evidence);
  if (!p) return <p>تعذر عرض دليل المهلة؛ لم تُعدّل البيانات المحفوظة.</p>;
  return <p role="note">سياسة المتابعة وقت التقديم · الإصدار {p.revision} · {p.days} أيام تقويمية من <bdi>{p.receivedOn}</bdi> إلى <bdi>{p.dueOn}</bdi> · <bdi>{p.timeZone}</bdi>. ليست وعد حل تلقائيًا؛ انتظار المعلومات وإعادة الفتح لا يعيدان المهلة.</p>;
}
function readDeadline(evidence: string) {
  try {
    const p = JSON.parse(evidence) as { revision: number; days: number; receivedOn: string; dueOn: string; timeZone: string };
    return p && Number.isInteger(p.revision) && Number.isInteger(p.days) && p.days >= 1 && p.days <= 30 && typeof p.receivedOn === "string" && typeof p.dueOn === "string" && typeof p.timeZone === "string" ? p : null;
  } catch { return null; }
}
