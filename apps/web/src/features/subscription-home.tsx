"use client";
import Image from "next/image";
import Link from "next/link";
import {BarChart3, ChevronDown} from "lucide-react";
import { useEffect, useState, type CSSProperties } from "react";
import { ErrorState } from "../components/dashboard-shell";
import { formatDateAr } from "../lib/formatters";
import styles from "./subscription-home.module.css";
const labels: Record<string, string> = { branches: "الفروع", sports: "الرياضات", packages: "الباقات", groups: "المجموعات", days: "أيام التدريب", ages: "الفئات العمرية", months: "نمو الاشتراكات" };
type Item = { key: string; name: string; total: number; detail?: string };
type Home = { academy: string; asOf: string; total: number; active: number; expiring: number; expired: number; players: number; newSubscriptions?: number | null; dimensions: string[]; branches: { id: string; name: string }[]; sports: { id: string; name: string; kind: string }[] };
function validOverview(value:Home):boolean{return !!value&&typeof value.academy==="string"&&typeof value.asOf==="string"&&Array.isArray(value.sports)&&Array.isArray(value.branches)&&Array.isArray(value.dimensions)&&[value.total,value.active,value.expiring,value.expired,value.players].every(n=>Number.isInteger(n)&&n>=0)&&value.expiring<=value.active;}
const n = (value: number) => new Intl.NumberFormat("ar-EG").format(value);
export function SubscriptionHome({ branchId = "", from, asOf, onBranchChange, owner = true, inlineOwnerScope = false, logoUrl, primaryColor, initialSport = "", initialDimension = "" }: { branchId?: string; from?: string; asOf?: string; inlineOwnerScope?: boolean; onBranchChange?: (id: string) => void; owner?: boolean; logoUrl?: string | null; primaryColor?: string; initialSport?: string; initialDimension?: string }) {
  const [analysisOpen,setAnalysisOpen]=useState(Boolean(initialDimension));
  const [localBranch, setLocalBranch] = useState(branchId); const [sport, setSport] = useState(initialSport); const [dimension, setDimension] = useState(initialDimension); const [revision, setRevision] = useState(0);
  const branch = onBranchChange ? branchId : localBranch;
  const query = new URLSearchParams({ ...branch ? { branchId: branch } : {}, ...sport ? { sportId: sport } : {}, ...asOf ? { asOf } : {}, ...from ? { from } : {} }).toString();
  const [result, setResult] = useState<{ key: string; data: Home }>(); const [error, setError] = useState<{ key: string; message: string }>();
  useEffect(() => { const abort = new AbortController(); fetch(`/api/v1/subscriptions/home?${query}`, { signal: abort.signal, cache: "no-store" }).then(async r => { if (!r.ok) throw Error(); return r.json() as Promise<Home>; }).then(data => { if(!validOverview(data))throw Error(); if (!abort.signal.aborted) { setResult({ key: query, data }); setError(undefined); } }).catch(() => { if (!abort.signal.aborted) setError({ key: query, message: "تعذر تحميل الاشتراكات داخل نطاقك." }); }); return () => abort.abort(); }, [query, revision]);
  const data = result?.key === query ? result.data : undefined;
  if (error?.key === query) return <section className="detail-card"><ErrorState message={error.message}/><button className="button button-secondary" type="button" onClick={() => { setError(undefined); setResult(undefined); setRevision(v => v + 1); }}>إعادة تحميل الاشتراكات</button></section>;
  if (!data) return <div className={styles.pending} role="status" aria-label="جارٍ تحميل الاشتراكات"><span/><span/><span/></div>;
  const kinds = [...new Set(data.sports.filter(s => !sport || s.id === sport).map(s => s.kind))]; const theme = kinds.length === 1 && kinds[0] === "Swimming" ? "swimming" : kinds.length === 1 && kinds[0] === "Football" ? "football" : "general";
  const list = (state = "all", detail?: { dimension: string; key: string }, uniquePlayers = false) => {
    const params = new URLSearchParams(query);
    params.set("asOf", data.asOf);
    params.set("state", state);
    if (inlineOwnerScope) params.set("origin", "owner-subscriptions");
    if (uniquePlayers) params.set("uniquePlayers", "true");
    if (detail) {
      params.set("dimension", detail.dimension);
      params.set("key", detail.key);
      if (detail.dimension === "months") {
        if (inlineOwnerScope && from) params.set("returnFrom", from);
        params.delete("from");
      }
    }
    return `/dashboard/players/subscriptions?${params}`;
  };
  const states = [
    { key: "current", title: "لا تنتهي قريبًا", count: data.active - data.expiring },
    { key: "expiring", title: "تنتهي قريبًا", count: data.expiring },
    { key: "expired", title: "منتهية", count: data.expired },
  ];
  const tint = /^#[a-f0-9]{6}$/i.test(primaryColor ?? "") ? primaryColor : "#3662AE";
  return <section className={styles.panel} aria-label="اشتراكات الأكاديمية">
    {!inlineOwnerScope && data.branches.length > 1 && <nav className={styles.filters} aria-label="فرع الاشتراكات">{[{ id: "", name: "كل الفروع" }, ...data.branches].map(b => <button key={b.id} aria-pressed={branch === b.id} onClick={() => { if (onBranchChange) onBranchChange(b.id); else setLocalBranch(b.id); }}>{b.name}</button>)}</nav>}
    {data.sports.length > 1 && <nav className={styles.filters} aria-label="رياضة الاشتراكات">{[{ id: "", name: "كل الرياضات" }, ...data.sports].map(s => <button key={s.id} aria-pressed={sport === s.id} onClick={() => setSport(s.id)}>{s.name}</button>)}</nav>}
    <div className={styles.hero} style={{ "--subscription-tint": tint } as CSSProperties}>
      <Image className={styles.art} src={`/home/subscriptions-${theme}.svg`} width={180} height={132} alt=""/>
      <div className={styles.brand}>{logoUrl === "/api/v1/academy/branding/logo" && <Image unoptimized src={logoUrl} width={32} height={32} alt="شعار الأكاديمية"/>}<span>{data.academy}</span></div>
      <h2>{owner ? "اشتراكات الأكاديمية" : "اشتراكات نطاقك"}</h2>
      <small>الحالة في <time dateTime={data.asOf}>{formatDateAr(data.asOf)}</time></small>
      <Link className={styles.total} href={list("active")} data-state="active" aria-label={`إجمالي الاشتراكات السارية، ${n(data.active)}`}><b>{n(data.active)}</b><span>إجمالي الاشتراكات السارية</span></Link>
      <div className={styles.states}>{states.map(s => <Link key={s.key} href={list(s.key)} data-state={s.key} aria-label={`${s.title}، ${n(s.count)}`}><b>{n(s.count)}</b><span>{s.title}</span></Link>)}</div>
      {inlineOwnerScope && data.newSubscriptions != null && <p className={styles.newCount}><b>{n(data.newSubscriptions)}</b> اشتراكات جديدة خلال الفترة</p>}
      <Link className={styles.players} href={list("all", undefined, true)} aria-label={`قائمة اللاعبين دون تكرار، ${n(data.players)}`}>قائمة اللاعبين <span>{n(data.players)} لاعب</span></Link>
    </div>
    {inlineOwnerScope && <button className={styles.analysis} type="button" aria-expanded={analysisOpen} onClick={() => setAnalysisOpen(value => !value)}>
      <BarChart3 size={23} aria-hidden="true"/><span><b>تحليل الاشتراكات</b><small>توزيع الاشتراكات ونموها</small></span><ChevronDown size={18} aria-hidden="true"/>
    </button>}
    {(!inlineOwnerScope || analysisOpen) && <nav className={styles.filters} aria-label="تفاصيل الاشتراكات">{data.dimensions.filter(d => labels[d]).map(d => <button key={d} aria-pressed={dimension === d} onClick={() => setDimension(v => v === d ? "" : d)}>{labels[d]}</button>)}</nav>}
    {data.dimensions.includes(dimension) && (!inlineOwnerScope || analysisOpen) && <SubscriptionBreakdown key={`${query}-${dimension}`} query={new URLSearchParams({ ...Object.fromEntries(new URLSearchParams(query)), asOf: data.asOf, dimension }).toString()} title={labels[dimension]} cumulative={dimension === "months"} href={key => list("all", { dimension, key })}/>}
  </section>;
}
function SubscriptionBreakdown({ query, title, cumulative, href }: { query: string; title: string; cumulative: boolean; href: (key: string) => string }) {
  const [items, setItems] = useState<Item[]>(); const [error, setError] = useState(false); const [revision, setRevision] = useState(0);
  useEffect(() => { const abort = new AbortController(); fetch(`/api/v1/subscriptions/home/breakdown?${query}`, { signal: abort.signal, cache: "no-store" }).then(async r => { if (!r.ok) throw Error(); return r.json() as Promise<{ items: Item[] }>; }).then(data => { if (!abort.signal.aborted) { setItems(data.items); setError(false); } }).catch(() => { if (!abort.signal.aborted) setError(true); }); return () => abort.abort(); }, [query, revision]);
  if (error) return <section className="detail-card"><ErrorState message="تعذر تحميل هذا التفصيل."/><button onClick={() => setRevision(v => v + 1)}>إعادة تحميل التفصيل</button></section>;
  if (!items) return <div className={styles.pending} role="status" aria-label="جارٍ تحميل التفصيل"><span/><span/></div>;
  const max = Math.max(0, ...items.map(i => i.total));
  return <section role="group" aria-label={title} className={`detail-card ${styles.breakdown}`}>
    <h3>{title}</h3>
    {cumulative && items.length > 0 && <small>التسجيلات التي بدأت اشتراكاتها حتى كل تاريخ.</small>}
    {!items.length && <p>لا توجد اشتراكات في هذا النطاق.</p>}
    {items.map(i => <Link href={href(i.key)} key={i.key}>
      <span><strong>{i.name}</strong>{" "}<b>{n(i.total)}</b></span>
      {i.detail && !cumulative && <small>{i.detail}</small>}
      <span className={styles.track} aria-hidden="true"><i style={{ width: `${max ? i.total / max * 100 : 0}%` }}/></span>
    </Link>)}
  </section>;
}
