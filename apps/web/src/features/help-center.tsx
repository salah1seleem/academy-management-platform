"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
import { BookOpen, Search, ArrowRight, FileText } from "lucide-react";
import "./help-center.css";
import { HelpVideoPlayer, type HelpVideo } from "./help-video";

type Text = { ar: string; en: string };
export type HelpLesson = { id: string; section: string; title: Text; summary: Text; version: number; contentKind: "WrittenGuide" | "AwaitingContent"; videoStatus: string; videoUrl: string | null; video?: HelpVideo | null; steps: Text[] | null };
const sections: Record<string, string> = { setup: "البدء والإعداد", overview: "قراءة الأداء", subscriptions: "اللاعبون والاشتراكات", operations: "التشغيل اليومي", development: "تطوير اللاعب", finance: "المالية والمستحقات" };
async function read<T>(path: string, signal: AbortSignal): Promise<T> {
  const r = await fetch(path, { cache: "no-store", signal });
  if (!r.ok) throw new Error(r.status === 403 || r.status === 404 ? "هذا الدليل غير متاح ضمن ميزات الأكاديمية وصلاحياتك الحالية." : r.status === 401 ? "سجل الدخول لعرض دليل دورك." : "تعذر تحميل الدليل. حاول مرة أخرى.");
  return r.json();
}
export function HelpCenter({ guardian = false, id }: { guardian?: boolean; id?: string }) {
  const root = guardian ? "/guardian/help" : "/dashboard/help";
  const [search, setSearch] = useState(""), [section, setSection] = useState("");
  const [query, setQuery] = useState({ search: "", section: "" }), [revision, setRevision] = useState(0);
  const key = JSON.stringify([id, query, revision]);
  const [response, setResponse] = useState<{ key: string; data?: HelpLesson[]; error?: string } | null>(null);
  const data = response?.key === key ? response.data : undefined, error = response?.key === key ? response.error : undefined;
  useEffect(() => {
    const c = new AbortController();
    const path = id ? `/api/v1/help/lessons/${encodeURIComponent(id)}` : `/api/v1/help/lessons?${new URLSearchParams(query)}`;
    read<HelpLesson | { items: HelpLesson[] }>(path, c.signal).then(value => { if (!c.signal.aborted) setResponse({ key, data: "items" in value ? value.items : [value] }); }).catch(e => { if (!c.signal.aborted) setResponse({ key, error: e.message }); });
    return () => c.abort();
  }, [id, query, revision, key]);
  useEffect(() => { const refresh = () => setRevision(x => x + 1); window.addEventListener("focus", refresh); return () => window.removeEventListener("focus", refresh); }, []);
  return <section className="help-center" dir="rtl">
    <Link className="help-back" href={id ? root : guardian ? "/guardian" : "/dashboard"}><ArrowRight size={18}/>{id ? "كل الأدلة" : "العودة للرئيسية"}</Link>
    <header className="help-hero"><span className="help-eyebrow"><BookOpen size={18}/>خطوة واضحة، في وقتها</span><h1>{id ? "دليل خطوة بخطوة" : "كيف نساعدك اليوم؟"}</h1><p>إرشادات للمسارات الحالية حسب دورك وصلاحياتك وميزات أكاديميتك.</p><small>أدلة مكتوبة وفيديوهات معتمدة عند توفرها. قراءة الدليل لا تمنح صلاحية تنفيذ.</small></header>
    {!id && <form className="help-search" onSubmit={e => { e.preventDefault(); setQuery({ search: search.trim(), section }); }}><label>البحث في الأدلة<input maxLength={100} value={search} placeholder="مثال: التجديد، الحضور، التقييم" onChange={e => setSearch(e.target.value)}/></label><label>القسم<select value={section} onChange={e => { setSection(e.target.value); setQuery({ search: search.trim(), section: e.target.value }); }}><option value="">كل الأقسام المتاحة</option>{Object.entries(sections).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label><button className="primary-button"><Search size={18}/>بحث</button><button className="secondary-button" type="button" onClick={() => { setSearch(""); setSection(""); setQuery({ search: "", section: "" }); }}>مسح</button></form>}
    {error ? <div role="alert" className="help-empty"><p>{error}</p><button onClick={() => setRevision(x => x + 1)}>إعادة المحاولة</button></div> : !data ? <p role="status">جارٍ تحميل الأدلة…</p> : !data.length ? <div className="help-empty" role="status">لا توجد أدلة ضمن هذا البحث وصلاحياتك الحالية. جرّب مسح البحث.</div> : id ? <article className="help-reading"><span className="help-eyebrow">{sections[data[0].section]} · إصدار {data[0].version}</span><h2>{data[0].title.ar}</h2><p>{data[0].summary.ar}</p>{data[0].videoStatus === "Published" && data[0].video && <HelpVideoPlayer key={`${data[0].video.id}:${revision}`} lesson={data[0].id} video={data[0].video}/>}{data[0].contentKind === "AwaitingContent" ? <p role="status">بانتظار استكمال المحتوى؛ لا يوجد شرح تنفيذي منشور بعد.</p> : <ol>{data[0].steps?.map((step, i) => <li key={i}>{step.ar}</li>)}</ol>}<aside><FileText size={18}/><span>الدليل المكتوب متاح دائمًا؛ يظهر الفيديو هنا فقط بعد نشره. ارجع للإدارة إذا لم يتوفر لك الإجراء داخل التطبيق.</span></aside></article> : <div className="help-grid">{data.map(lesson => <Link className="help-card" key={lesson.id} href={`${root}/${lesson.id}`}><span className="help-eyebrow"><FileText size={18}/>{sections[lesson.section]}</span><h2>{lesson.title.ar}</h2><p>{lesson.summary.ar}</p><span className="help-card-action">{lesson.contentKind === "AwaitingContent" ? "بانتظار استكمال المحتوى" : "اقرأ الخطوات"}<ArrowRight size={16}/></span></Link>)}</div>}
  </section>;
}
