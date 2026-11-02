"use client";
import Link from "next/link";
import { useEffect, useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import { BookOpen, Film, ImagePlus, Archive, CheckCircle2 } from "lucide-react";
import { platformRequest } from "../../../features/platform-api";
import "../../../features/help-center.css";

type Revision = { id: string; lessonId: string; revision: number; editVersion: number; state: string; title: string; description: string; alternativeText: string; hasVideo: boolean; hasThumbnail: boolean; durationSeconds: number | null };
type Lesson = { id: string; title: { ar: string }; roles: string[]; feature: string | null; sport: string | null };
const states: Record<string, string> = { Draft: "مسودة", Published: "منشور", Archived: "مؤرشف" };
const roles: Record<string, string> = { AcademyOwner: "المالك", AcademyAdmin: "الإداري", Coach: "المدرب", Guardian: "ولي الأمر" };
export default function PlatformHelpVideos() {
  const router = useRouter();
  const [lessons, setLessons] = useState<Lesson[]>([]), [revisions, setRevisions] = useState<Revision[]>([]);
  const [lesson, setLesson] = useState(""), [selected, setSelected] = useState<Revision | null>(null);
  const [busy, setBusy] = useState(false), [ready, setReady] = useState(false), [message, setMessage] = useState("");
  const [preview, setPreview] = useState<string | null>(null);
  async function load() {
    const r = await fetch("/api/v1/platform/help-videos", { cache: "no-store" });
    if (r.status === 401 || r.status === 403) { router.replace("/platform/login"); return; }
    if (!r.ok) throw new Error("تعذر تحميل مكتبة الفيديو. أعد المحاولة.");
    const data = await r.json(); setLessons(data.lessons); setRevisions(data.revisions); setReady(true);
  }
  useEffect(() => {
    const c = new AbortController();
    void fetch("/api/v1/platform/help-videos", { cache: "no-store", signal: c.signal }).then(async r => {
      if (r.status === 401 || r.status === 403) { router.replace("/platform/login"); return; }
      if (!r.ok) throw new Error("تعذر تحميل مكتبة الفيديو. أعد المحاولة.");
      const data = await r.json();
      if (!c.signal.aborted) { setLessons(data.lessons); setRevisions(data.revisions); setReady(true); }
    }).catch(e => { if (!c.signal.aborted) setMessage(e.message); });
    return () => c.abort();
  }, [router]);
  async function perform(work: () => Promise<void>) {
    if (busy) return; setBusy(true); setMessage(""); setPreview(null);
    try { await work(); await load(); }
    catch (e) { setMessage(e instanceof Error ? e.message : "تعذر حفظ التغيير."); }
    finally { setBusy(false); }
  }
  function metadata(e: FormEvent<HTMLFormElement>) {
    e.preventDefault(); const data = new FormData(e.currentTarget);
    const values = { title: data.get("title"), description: data.get("description"), alternativeText: data.get("alternative") };
    void perform(async () => {
      const response = await platformRequest(`/api/v1/platform/help-videos${selected ? "/" + selected.id : ""}`, selected ? "PUT" : "POST", selected ? { version: selected.editVersion, metadata: values } : { lessonId: lesson, metadata: values });
      setSelected(await response.json()); setMessage(selected ? "تم حفظ بيانات المسودة." : "تم إنشاء المسودة. ارفع الفيديو والصورة ثم راجعهما قبل النشر.");
    });
  }
  function upload(e: FormEvent<HTMLFormElement>, kind: string) {
    e.preventDefault(); if (!selected) return;
    const body = new FormData(e.currentTarget); body.set("version", String(selected.editVersion));
    void perform(async () => { setSelected(await (await platformRequest(`/api/v1/platform/help-videos/${selected.id}/upload/${kind}`, "POST", body)).json()); setMessage("تم رفع الملف وفحص تنسيقه. لم يُنشر بعد."); });
  }
  function transition(target: string) {
    if (!selected || !window.confirm(target === "Published" ? "نشر هذا الإصدار بدلًا من الإصدار المنشور السابق؟" : "أرشفة هذا الإصدار وإيقاف عرضه للمتعلمين؟")) return;
    void perform(async () => { setSelected(await (await platformRequest(`/api/v1/platform/help-videos/${selected.id}/transition`, "POST", { version: selected.editVersion, target })).json()); setMessage(target === "Published" ? "تم نشر الفيديو للجمهور المصرح له فقط." : "تمت الأرشفة. الدليل المكتوب لم يُحذف."); });
  }
  const topic = lessons.find(x => x.id === lesson);
  return <main className="platform-shell help-center" dir="rtl">
    <Link className="help-back" href="/platform">العودة لإدارة المنصة</Link>
    <header className="help-hero"><span className="help-eyebrow"><BookOpen/>مكتبة شروحات المنصة</span><h1>شرح عملي، وإصدار موثوق</h1><p>احفظ مسودة، ارفع المحتوى وراجعه، ثم انشره. لا يرى المتعلمون المسودات أو الأرشيف.</p><small>الجمهور والرياضة والميزة تُورث من الدليل الحالي؛ اختيار المالك قد يضيّق الجمهور فقط. النشر لا يمنح صلاحيات تنفيذ.</small></header>
    {message && <p role="status" className="notice">{message}</p>}
    {!ready ? <button disabled={busy} onClick={() => void perform(load)}>إعادة تحميل المكتبة</button> : <>
      <div className="help-topic"><label htmlFor="help-lesson">الدليل</label><select id="help-lesson" value={lesson} onChange={e => { setLesson(e.target.value); setSelected(null); setPreview(null); setMessage(""); }}><option value="">اختر الدليل</option>{lessons.map(x => <option key={x.id} value={x.id}>{x.title.ar}</option>)}</select></div>
      {topic && <><p className="muted">الجمهور المؤهل: {topic.roles.map(x => roles[x] ?? x).join("، ")} · {topic.sport === "Swimming" ? "للسباحة فقط" : "حسب ميزات الدليل وصلاحياته"}</p>
        <div className="help-revisions">{revisions.filter(x => x.lessonId === lesson).map(row => <button key={row.id} aria-pressed={selected?.id === row.id} onClick={() => { setSelected(row); setPreview(null); setMessage(""); }}><strong>إصدار {row.revision}</strong><span>{states[row.state]}</span><small>{row.title}</small></button>)}</div>
        <button className="secondary-button" disabled={busy || revisions.some(x => x.lessonId === lesson && x.state === "Draft")} onClick={() => { setSelected(null); setPreview(null); }}>مسودة إصدار جديد</button>
        {(!selected || selected.state === "Draft") && <form className="form-shell help-reading help-video-form" key={`${lesson}:${selected?.id ?? "new"}:${selected?.editVersion ?? 0}`} onSubmit={metadata}>
          <h2>{selected ? `بيانات الإصدار ${selected.revision}` : "إنشاء مسودة"}</h2>
          <label>عنوان الفيديو<input name="title" required maxLength={160} defaultValue={selected?.title ?? topic.title.ar}/></label>
          <label>الوصف<textarea name="description" required maxLength={1600} rows={3} defaultValue={selected?.description ?? ""}/></label>
          <label>بديل نصي مختصر للمحتوى<textarea name="alternative" required maxLength={300} rows={2} defaultValue={selected?.alternativeText ?? ""}/></label>
          <button disabled={busy || !selected && revisions.some(x => x.lessonId === lesson && x.state === "Draft")}>حفظ المسودة</button>
        </form>}
        {selected && <section className="help-reading help-video-form"><h2>{selected.title} · {states[selected.state]}</h2><p>{selected.description}</p><p>ملخص نصي: {selected.alternativeText}</p>
          <div className="help-media-status"><span><Film/>{selected.hasVideo ? `فيديو محفوظ · ${selected.durationSeconds} ثانية` : "الفيديو لم يُرفع"}</span><span><ImagePlus/>{selected.hasThumbnail ? "الصورة المصغرة محفوظة" : "الصورة المصغرة لم تُرفع"}</span></div>
          {selected.state === "Draft" && <div className="help-upload-grid">
            <form onSubmit={e => upload(e, "video")}><label>الفيديو · MP4 / H.264 · حتى 25 MB و30 دقيقة<input name="file" type="file" accept="video/mp4" required/></label><button disabled={busy}>رفع الفيديو وفحصه</button></form>
            <form onSubmit={e => upload(e, "thumbnail")}><label>صورة مصغرة · PNG · حتى 2 MB و1920 بكسل<input name="file" type="file" accept="image/png" required/></label><button disabled={busy}>رفع الصورة وفحصها</button></form>
          </div>}
          {selected.hasVideo && <button disabled={busy} className="secondary-button" onClick={() => setPreview(selected.id)}>معاينة الفيديو الخاص</button>}
          {preview === selected.id && <video className="help-video" key={`${selected.id}:${selected.editVersion}`} src={`/api/v1/platform/help-videos/${selected.id}/preview/video`} poster={selected.hasThumbnail ? `/api/v1/platform/help-videos/${selected.id}/preview/thumbnail` : undefined} controls preload="metadata" onError={() => setMessage("تعذر تشغيل المعاينة. تحقق من جلسة المنصة وصلاحية ملف الفيديو.")}/>}
          <div className="help-media-status">{selected.state === "Draft" && <button disabled={busy || !selected.hasVideo || !selected.hasThumbnail} onClick={() => transition("Published")}><CheckCircle2/>نشر الإصدار</button>}{selected.state !== "Archived" && <button disabled={busy} className="secondary-button" onClick={() => transition("Archived")}><Archive/>أرشفة الإصدار</button>}</div>
          {selected.state === "Published" && <p className="muted">هذا الإصدار ثابت. لتعديل المحتوى، أنشئ مسودة جديدة؛ يبقى هذا الفيديو منشورًا حتى نشر البديل أو أرشفته.</p>}
        </section>}
      </>}
    </>}
  </main>;
}
