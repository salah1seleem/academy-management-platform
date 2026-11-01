"use client";
import Image from "next/image";
import Link from "next/link";
import { useEffect, useState, type CSSProperties } from "react";
import { ErrorState } from "../components/dashboard-shell";
import { formatDateAr } from "../lib/formatters";
import styles from "./subscription-home.module.css";
const labels: Record<string, string> = { branches: "الفروع", sports: "الرياضات", packages: "الباقات", groups: "المجموعات", days: "أيام التدريب", ages: "الفئات العمرية", months: "نمو الاشتراكات" };
type Item = { key: string; name: string; total: number; detail?: string };
type Home = { academy: string; asOf: string; total: number; active: number; expiring: number; expired: number; players: number; dimensions: string[]; branches: { id: string; name: string }[]; sports: { id: string; name: string; kind: string }[] };
function validOverview(value:Home):boolean{return !!value&&typeof value.academy==="string"&&typeof value.asOf==="string"&&Array.isArray(value.sports)&&Array.isArray(value.branches)&&Array.isArray(value.dimensions)&&[value.total,value.active,value.expiring,value.expired,value.players].every(n=>Number.isInteger(n)&&n>=0);}
const n = (value: number) => new Intl.NumberFormat("ar-EG").format(value);
export function SubscriptionHome({ branchId = "", asOf, onBranchChange, owner = true, logoUrl, primaryColor }: { branchId?: string; asOf?: string; onBranchChange?: (id: string) => void; owner?: boolean; logoUrl?: string | null; primaryColor?: string }) {
  const [localBranch, setLocalBranch] = useState(branchId); const [sport, setSport] = useState(""); const [dimension, setDimension] = useState(""); const [revision, setRevision] = useState(0);
  const branch = onBranchChange ? branchId : localBranch;
  const query = new URLSearchParams({ ...branch ? { branchId: branch } : {}, ...sport ? { sportId: sport } : {}, ...asOf ? { asOf } : {} }).toString();
  const [result, setResult] = useState<{ key: string; data: Home }>(); const [error, setError] = useState<{ key: string; message: string }>();
  useEffect(() => { const abort = new AbortController(); fetch(`/api/v1/subscriptions/home?${query}`, { signal: abort.signal, cache: "no-store" }).then(async r => { if (!r.ok) throw Error(); return r.json() as Promise<Home>; }).then(data => { if(!validOverview(data))throw Error(); if (!abort.signal.aborted) { setResult({ key: query, data }); setError(undefined); } }).catch(() => { if (!abort.signal.aborted) setError({ key: query, message: "تعذر تحميل الاشتراكات داخل نطاقك." }); }); return () => abort.abort(); }, [query, revision]);
  const data = result?.key === query ? result.data : undefined;
  if (error?.key === query) return <section className="detail-card"><ErrorState message={error.message}/><button onClick={() => setRevision(v => v + 1)}>إعادة تحميل الاشتراكات</button></section>;
  if (!data) return <div className={styles.pending} role="status" aria-label="جارٍ تحميل الاشتراكات"><span/><span/><span/></div>;
  const kinds = [...new Set(data.sports.filter(s => !sport || s.id === sport).map(s => s.kind))]; const theme = kinds.length === 1 && kinds[0] === "Swimming" ? "swimming" : kinds.length === 1 && kinds[0] === "Football" ? "football" : "general";
  const list = (state = "", dim?: string, key?: string) => `/dashboard/players/subscriptions?${new URLSearchParams({ ...Object.fromEntries(new URLSearchParams(query)), asOf: data.asOf, state: state || "all", ...dim ? { dimension: dim, key: key! } : {} })}`;
  const tint = /^#[a-f0-9]{6}$/i.test(primaryColor ?? "") ? primaryColor : "#3662AE";
  return <section className={styles.panel} aria-label="اشتراكات الأكاديمية">
    {data.branches.length > 1 && <nav className={styles.filters} aria-label="فرع الاشتراكات">{[{ id: "", name: "كل الفروع" }, ...data.branches].map(b => <button key={b.id} aria-pressed={branch === b.id} onClick={() => { if (onBranchChange) onBranchChange(b.id); else setLocalBranch(b.id); }}>{b.name}</button>)}</nav>}
    {data.sports.length > 1 && <nav className={styles.filters} aria-label="رياضة الاشتراكات">{[{ id: "", name: "كل الرياضات" }, ...data.sports].map(s => <button key={s.id} aria-pressed={sport === s.id} onClick={() => setSport(s.id)}>{s.name}</button>)}</nav>}
    <div className={styles.hero} style={{ "--subscription-tint": tint } as CSSProperties}>
      <Image className={styles.art} src={`/home/subscriptions-${theme}.svg`} width={180} height={132} alt=""/>
      <div className={styles.brand}>{logoUrl === "/api/v1/academy/branding/logo" && <Image unoptimized src={logoUrl} width={32} height={32} alt="شعار الأكاديمية"/>}<span>{data.academy}</span></div>
      <h2>{owner ? "اشتراكات الأكاديمية" : "اشتراكات نطاقك"}</h2><Link className={styles.total} href={list()}><b>{n(data.total)}</b><span>إجمالي التسجيلات</span></Link>
      <div className={styles.states}>{([{ key: "active", title: "فعالة" }, { key: "expiring", title: "تنتهي قريبًا" }, { key: "expired", title: "منتهية" }] as const).map(s => <Link key={s.key} href={list(s.key)} data-state={s.key}><b>{n(data[s.key])}</b><span>{s.title}</span></Link>)}</div>
      <p>تسجيلات رياضية لها اشتراك · قرب الانتهاء ضمن الفعالة</p><small>بتاريخ {formatDateAr(data.asOf)} · {n(data.players)} لاعب مميز</small>
    </div>
    <nav className={styles.filters} aria-label="تفاصيل الاشتراكات">{data.dimensions.filter(d => labels[d]).map(d => <button key={d} aria-pressed={dimension === d} onClick={() => setDimension(v => v === d ? "" : d)}>{labels[d]}</button>)}</nav>
    {dimension && <SubscriptionBreakdown key={`${query}-${dimension}`} query={new URLSearchParams({ ...Object.fromEntries(new URLSearchParams(query)), asOf: data.asOf, dimension }).toString()} title={labels[dimension]} href={key => list("", dimension, key)}/>}
  </section>;
}
function SubscriptionBreakdown({ query, title, href }: { query: string; title: string; href: (key: string) => string }) {
  const [items, setItems] = useState<Item[]>(); const [error, setError] = useState(false); const [revision, setRevision] = useState(0);
  useEffect(() => { const abort = new AbortController(); fetch(`/api/v1/subscriptions/home/breakdown?${query}`, { signal: abort.signal, cache: "no-store" }).then(async r => { if (!r.ok) throw Error(); return r.json() as Promise<{ items: Item[] }>; }).then(data => { if (!abort.signal.aborted) { setItems(data.items); setError(false); } }).catch(() => { if (!abort.signal.aborted) setError(true); }); return () => abort.abort(); }, [query, revision]);
  if (error) return <section className="detail-card"><ErrorState message="تعذر تحميل هذا التفصيل."/><button onClick={() => setRevision(v => v + 1)}>إعادة تحميل التفصيل</button></section>;
  if (!items) return <div className={styles.pending} role="status" aria-label="جارٍ تحميل التفصيل"><span/><span/></div>;
  const max = Math.max(0, ...items.map(i => i.total));
  return <section role="group" aria-label={title} className={`detail-card ${styles.breakdown}`}><h3>{title}</h3>{!items.length && <p>لا توجد اشتراكات في هذا النطاق.</p>}{items.map(i => <Link href={href(i.key)} key={i.key}><span><strong>{i.name}</strong><b>{n(i.total)}</b></span>{i.detail && <small>{i.detail}</small>}<span className={styles.track}><i style={{ width: `${max ? i.total / max * 100 : 0}%` }}/></span></Link>)}</section>;
}
