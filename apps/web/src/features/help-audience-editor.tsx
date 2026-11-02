"use client";
import "./help-audience-editor.css";
export type HelpOption = { id: string; title: { ar: string; en: string }; roles: string[]; feature: string | null };
const roleNames: Record<string,string> = { AcademyOwner: "المالك", AcademyAdmin: "الإداري", Coach: "المدرب", Guardian: "ولي الأمر" };
export function HelpAudienceEditor({ lessons, value, disabled, onChange }: { lessons: HelpOption[]; value?: Record<string,string[]> | null; disabled: boolean; onChange: (next: Record<string,string[]>) => void }) {
  return <fieldset className="help-audience-editor"><legend>جمهور دليل الاستخدام</legend><p>اختيار الأدوار يضيّق إتاحة الدليل فقط. صلاحيات المستخدم وترخيص الميزة يظلان مطلوبين، ولا يمنح الدليل حضورًا أو أي إجراء. احفظ مسودة الإعدادات ثم تحقق منها وجهّزها وفعّلها لتسري التغييرات.</p>
    <p>الفيديوهات لم تُرفع بعد؛ هذه الإعدادات للأدلة المكتوبة الحالية.</p>
    <div className="help-audience-grid">{lessons.map(lesson => { const selected = value?.[lesson.id] ?? lesson.roles; return <section className="detail-card" key={lesson.id}><h3>{lesson.title.ar}</h3><div className="row-actions">{lesson.roles.map(role => <label className="policy-switch" key={role}><input type="checkbox" disabled={disabled} aria-label={`${lesson.title.ar} — ${roleNames[role]}`} checked={selected.includes(role)} onChange={e => onChange({ ...value, [lesson.id]: e.target.checked ? [...selected, role] : selected.filter(r => r !== role) })}/>{roleNames[role]}</label>)}</div><small>{selected.length ? "يعرض للمؤهلين فقط من الأدوار المختارة" : "مخفي عن كل الأدوار في الأكاديمية"}</small><p><button type="button" disabled={disabled || value?.[lesson.id] === undefined} onClick={() => { const next = { ...value }; delete next[lesson.id]; onChange(next); }}>استعادة جمهور المنتج</button></p></section>; })}</div>
  </fieldset>;
}
