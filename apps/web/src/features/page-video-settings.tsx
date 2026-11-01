"use client";
import { useEffect, useRef, useState } from "react";
import { ErrorState, LoadingState } from "../components/dashboard-shell";
import { announceSaveSuccess, csrfRequest, DashboardRequestError } from "./dashboard-api";
type Video = { role: string; screen: string; title: string; url: string; isActive: boolean };
const roles: Record<string, string> = { AcademyOwner: "المالك", AcademyAdmin: "الإداري", Coach: "المدرب", Guardian: "ولي الأمر" };
const screens: Record<string, string> = { home: "الرئيسية", operations: "العمليات", more: "المزيد", reports: "التقارير", matches: "المباريات", players: "اللاعبون", attendance: "الحضور", nutrition: "التغذية والصحة", evaluations: "التقييمات", staff: "الموظفون والصلاحيات", import: "استيراد البيانات" };
class VerificationFailed extends Error {}
async function readVideos(signal?: AbortSignal): Promise<Video[]> {
  const response = await fetch("/api/v1/manage/page-help", { signal, cache: "no-store" });
  if (!response.ok) throw new VerificationFailed("تعذر تحميل إعدادات فيديوهات الشرح أو لا توجد صلاحية.");
  const data = await response.json() as { items: Video[] };
  if (!Array.isArray(data.items)) throw new VerificationFailed("تعذر قراءة قائمة فيديوهات الشرح.");
  return data.items;
}
export function PageVideoSettings() {
  const mounted = useRef(true);
  const [items, setItems] = useState<Video[] | null>(null), [form, setForm] = useState<Video | null>(null);
  const [error, setError] = useState(""), [busy, setBusy] = useState(false), [message, setMessage] = useState("");
  useEffect(() => {
    mounted.current = true;
    const controller = new AbortController();
    readVideos(controller.signal).then(data => { if (!controller.signal.aborted) setItems(data); })
      .catch(() => { if (!controller.signal.aborted) setError("تعذر تحميل إعدادات فيديوهات الشرح أو لا توجد صلاحية."); });
    return () => { mounted.current = false; controller.abort(); };
  }, []);
  function edit(value: Video) { if (busy) return; setForm({ ...value }); setMessage(""); setError(""); }
  async function save() {
    if (!form || busy) return;
    const submitted = { ...form, title: form.title.trim() };
    setBusy(true); setError(""); setMessage("");
    try {
      await csrfRequest("/api/v1/manage/page-help", "PUT", submitted, {}, { announce: false });
      if (!mounted.current) return;
      const stored = await readVideos();
      if (!mounted.current) return;
      const matches = stored.filter(video => video.role === submitted.role && video.screen === submitted.screen);
      if (matches.length !== 1 || matches[0].title !== submitted.title || matches[0].url !== submitted.url || matches[0].isActive !== submitted.isActive) {
        throw new VerificationFailed("لم تتطابق إعدادات الفيديو المحفوظة. أعد المحاولة بنفس الاختيار أو حدّث القائمة.");
      }
      setItems(stored); setForm(null); setMessage("تم حفظ فيديو الشرح والتحقق من إعداداته.");
      announceSaveSuccess("تم حفظ فيديو الشرح والتحقق من إعداداته.");
    } catch (caught) {
      if (mounted.current) setError(caught instanceof DashboardRequestError || caught instanceof VerificationFailed ? caught.message : "تعذر حفظ فيديو الشرح أو التحقق منه. حاول مجددًا.");
    } finally { if (mounted.current) setBusy(false); }
  }
  return <section id="page-videos" className="detail-card">
    <h2>فيديوهات شرح الصفحات</h2><details><summary>اختيار فيديو الشرح</summary><p>أضف لكل دور وصفحة رابط فيديو مباشرًا وآمنًا، بدل رابط صفحة مشاهدة.</p></details>
    {error && <ErrorState message={error} />}{message && <p role="status">{message}</p>}
    {!items && !error && <LoadingState />}
    <button className="secondary-button" disabled={busy || !items} onClick={() => edit({ role: "AcademyOwner", screen: "home", title: "", url: "", isActive: true })}>إضافة فيديو شرح</button>
    {items?.map(video => <article className="profile-row" key={`${video.role}-${video.screen}`}><strong>{video.title}</strong><span>{roles[video.role]} · {screens[video.screen]} · {video.isActive ? "فعال" : "موقوف"}</span><button disabled={busy} onClick={() => edit(video)}>تعديل</button></article>)}
    {form && <form className="form-grid" onSubmit={event => { event.preventDefault(); void save(); }}>
      <label>الدور<select disabled={busy} value={form.role} onChange={e => setForm({ ...form, role: e.target.value })}>{Object.entries(roles).map(([key, label]) => <option value={key} key={key}>{label}</option>)}</select></label>
      <label>الصفحة<select disabled={busy} value={form.screen} onChange={e => setForm({ ...form, screen: e.target.value })}>{Object.entries(screens).map(([key, label]) => <option value={key} key={key}>{label}</option>)}</select></label>
      <label>عنوان الشرح<input disabled={busy} required maxLength={120} value={form.title} onChange={e => setForm({ ...form, title: e.target.value })} /></label>
      <label>رابط الفيديو<input disabled={busy} required type="url" maxLength={2000} dir="ltr" value={form.url} onChange={e => setForm({ ...form, url: e.target.value })} /></label>
      <label><input disabled={busy} type="checkbox" checked={form.isActive} onChange={e => setForm({ ...form, isActive: e.target.checked })} />فعال</label>
      <button className="primary-button" disabled={busy}>{busy ? "جارٍ الحفظ والتحقق…" : "حفظ الفيديو"}</button><button type="button" disabled={busy} onClick={() => setForm(null)}>رجوع للقائمة</button>
    </form>}
  </section>;
}
