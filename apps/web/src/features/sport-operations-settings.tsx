"use client";

import Link from "next/link";
import {useSearchParams} from "next/navigation";
import {FormEvent, useEffect, useRef, useState} from "react";
import {ErrorState, LoadingState, PageHeader} from "../components/dashboard-shell";
import {announceSaveSuccess, csrfRequest, DashboardRequestError} from "./dashboard-api";

type Kind = "General" | "Football" | "Swimming";
type Sport = {id: string; name: string; kind: Kind; isActive: boolean; version: number};
type Policy = {branchId: string; enabled: boolean; privateTrainingEnabled: boolean; openSwimEnabled: boolean; dayPassEnabled: boolean; version: number};
type Data = {sports: Sport[]; branches: {id: string; name: string}[]; facilitiesEnabled: boolean};
type Detail = {policies: Policy[]; history: {branchId: string | null; createdAtUtc: string}[]};
type Change = {type: "kind"; sportId: string; body: {kind: Kind; version: number}} | {type: "policy"; sportId: string; branchId: string; branchName: string; body: Omit<Policy, "branchId">};
const kinds: Record<Kind, string> = {General: "رياضة عامة", Football: "كرة قدم", Swimming: "سباحة"};
const policyFields = ["enabled", "privateTrainingEnabled", "openSwimEnabled", "dayPassEnabled"] as const;
async function metadata(signal?: AbortSignal): Promise<Data> {
  const r = await fetch("/api/v1/manage/sport-settings", {cache: "no-store", signal});
  if (!r.ok) throw new DashboardRequestError("خصائص الرياضات متاحة للمالك فقط. تعذر تحميل الإعدادات.", r.status);
  return r.json();
}
async function swimming(id: string, signal?: AbortSignal): Promise<Detail> {
  const r = await fetch(`/api/v1/manage/sport-settings/${id}/swimming`, {cache: "no-store", signal});
  if (!r.ok) throw new DashboardRequestError("تعذر تحميل إعدادات السباحة. راجع الصلاحية وحاول مرة أخرى.", r.status);
  return r.json();
}
const failure = (e: unknown, fallback: string) => e instanceof DashboardRequestError ? e.message : fallback;
export function SportOperationsSettings() {
  const initialSport = useSearchParams()?.get("sportId") ?? "";
  const [data, setData] = useState<Data | null>(null), [selected, setSelected] = useState(initialSport), [kind, setKind] = useState<Kind>("General");
  const [snapshot, setSnapshot] = useState<{sportId: string; detail: Detail} | null>(null);
  const [error, setError] = useState(""), [notice, setNotice] = useState(""), [busy, setBusy] = useState(false), [pending, setPending] = useState<Change | null>(null), [reload, setReload] = useState(0);
  const mounted = useRef(true), writing = useRef(false);
  const sport = data?.sports.find(x => x.id === selected), detail = snapshot?.sportId === sport?.id ? snapshot?.detail : null;
  const locked = busy || pending !== null;
  useEffect(() => {mounted.current = true; return () => {mounted.current = false;};}, []);
  useEffect(() => {
    const c = new AbortController();
    metadata(c.signal).then(value => {if (!c.signal.aborted) {setData(value); setKind(value.sports.find(s => s.id === initialSport)?.kind ?? "General");}})
      .catch(e => {if (!c.signal.aborted) setError(failure(e, "تعذر تحميل خصائص الرياضات."));});
    return () => c.abort();
  }, [initialSport]);
  useEffect(() => {
    if (sport?.kind !== "Swimming") return;
    const id = sport.id, c = new AbortController();
    swimming(id, c.signal).then(value => {if (!c.signal.aborted) setSnapshot({sportId: id, detail: value});})
      .catch(e => {if (!c.signal.aborted) setError(failure(e, "تعذر تحميل إعدادات السباحة."));});
    return () => c.abort();
  }, [sport?.id, sport?.kind]);
  function select(id: string) {setSelected(id); setKind(data?.sports.find(x => x.id === id)?.kind ?? "General"); setSnapshot(null); setError(""); setNotice("");}
  async function verify(change: Change) {
    if (change.type === "kind") {
      const value = await metadata();
      if (!mounted.current) return;
      const saved = value.sports.find(s => s.id === change.sportId);
      if (!saved || saved.kind !== change.body.kind || saved.version <= 0) throw new DashboardRequestError("لم يتطابق نوع الرياضة المقروء مع الاختيار المحفوظ. أعد التحقق.", 0);
      setData(value); setKind(saved.kind);
    } else {
      const value = await swimming(change.sportId);
      if (!mounted.current) return;
      const saved = value.policies.find(p => p.branchId === change.branchId);
      if (!saved || saved.version <= 0 || !policyFields.every(k => saved[k] === change.body[k])) throw new DashboardRequestError("لم تتطابق إعدادات الفرع المقروءة مع القيم المرسلة. أعد التحقق.", 0);
      setSnapshot({sportId: change.sportId, detail: value});
    }
    if (!mounted.current) return;
    setPending(null);
    const message = change.type === "kind" ? "تم حفظ نوع الرياضة." : `تم حفظ إعدادات ${change.branchName}.`;
    setNotice(message); announceSaveSuccess(message);
  }
  function reject(e: unknown) {
    if (!mounted.current) return;
    if (e instanceof DashboardRequestError && [401, 403].includes(e.status)) {setData(null); setSnapshot(null);}
    setError(failure(e, "تعذر تأكيد الحفظ. أعد التحقق من الإعدادات المحفوظة."));
  }
  async function begin(change: Change) {
    if (writing.current || pending) return;
    writing.current = true; setBusy(true); setPending(change); setError(""); setNotice("");
    try {
      const path = change.type === "kind" ? `/api/v1/manage/sport-settings/${change.sportId}/kind` : `/api/v1/manage/sport-settings/${change.sportId}/swimming/${change.branchId}`;
      try {await csrfRequest(path, "PUT", change.body, {}, {announce: false});}
      catch (e) {if (mounted.current && e instanceof DashboardRequestError && e.status >= 400 && e.status < 500 && e.status !== 408) setPending(null); throw e;}
      if (mounted.current) await verify(change);
    } catch (e) {reject(e);} finally {writing.current = false; if (mounted.current) setBusy(false);}
  }
  async function recheck() {
    if (!pending || writing.current) return;
    writing.current = true; setBusy(true); setError(""); setNotice("");
    try {await verify(pending);} catch (e) {reject(e);} finally {writing.current = false; if (mounted.current) setBusy(false);}
  }
  async function reloadCurrent() {
    if (writing.current) return;
    writing.current = true; setBusy(true); setError(""); setNotice("");
    try {
      const value = await metadata(), chosen = value.sports.find(s => s.id === selected);
      const policy = chosen?.kind === "Swimming" ? await swimming(chosen.id) : null;
      if (!mounted.current) return;
      setData(value); setKind(chosen?.kind ?? "General"); setSnapshot(chosen && policy ? {sportId: chosen.id, detail: policy} : null); setPending(null); setReload(v => v + 1);
    } catch (e) {reject(e);} finally {writing.current = false; if (mounted.current) setBusy(false);}
  }
  function saveKind(e: FormEvent<HTMLFormElement>) {e.preventDefault(); if (sport) void begin({type: "kind", sportId: sport.id, body: {kind, version: sport.version}});}
  function savePolicy(e: FormEvent<HTMLFormElement>, branchId: string, branchName: string, version: number) {
    e.preventDefault(); if (!sport) return; const fields = new FormData(e.currentTarget);
    void begin({type: "policy", sportId: sport.id, branchId, branchName, body: {enabled: fields.has("enabled"), privateTrainingEnabled: fields.has("private"), openSwimEnabled: fields.has("open"), dayPassEnabled: fields.has("day"), version}});
  }
  return <>
    <PageHeader title="خصائص الرياضات" context="الإعدادات / تشغيل الرياضات" description="اضبط الرياضة مرة واحدة، ثم خدمات كل فرع على حدة." />
    <Link className="secondary-button" href="/dashboard/settings">العودة إلى الإعدادات</Link>
    {error && <ErrorState message={error} />}{notice && <p className="success-message" role="status">{notice}</p>}
    {pending && <p>القيم المرسلة مقفلة حتى التحقق من الحفظ أو إعادة قراءة الوضع الحالي.</p>}
    {(pending || error) && <div className="form-actions">{pending && <button disabled={busy} onClick={() => void recheck()}>إعادة التحقق من الحفظ</button>}<button disabled={busy} onClick={() => void reloadCurrent()}>إعادة تحميل الإعدادات الحالية</button></div>}
    {!data ? !error && <LoadingState /> : <>
      <section className="detail-card sport-settings-picker"><label>الرياضة<select disabled={locked} value={selected} onChange={e => select(e.target.value)}><option value="">اختر الرياضة</option>{data.sports.map(x => <option key={x.id} value={x.id}>{x.name}{x.isActive ? "" : " — موقوفة"}</option>)}</select></label>
        {sport && <form className="sport-kind-form" onSubmit={saveKind}><label>نوع الرياضة<select disabled={locked} value={kind} onChange={e => setKind(e.target.value as Kind)}>{Object.entries(kinds).map(([key, label]) => <option value={key} key={key}>{label}</option>)}</select></label><p className="muted">يحدد إعدادات التشغيل، ولا يغيّر الاسم أو الباقات. النوع المرتبط بمرفق أو إعداد محفوظ لا يُبدّل.</p><button className="primary-button" disabled={locked || kind === sport.kind}>حفظ نوع الرياضة</button></form>}
      </section>
      {sport && sport.kind !== "Swimming" && data.facilitiesEnabled && <section className="detail-card"><h2>مرافق الرياضة</h2><Link href="/dashboard/facilities">الملاعب والمناطق والحجوزات</Link></section>}
      {sport?.kind === "Swimming" && <section className="detail-card" aria-label="إعدادات السباحة"><h2>إعدادات السباحة حسب الفرع</h2><p className="muted">إيقاف الفرع يمنع عمليات سباحة جديدة ويحفظ سجلاته. الأسعار والنسب في الاتفاقات المالية؛ اعتماد السعة لكل مورد في المرافق.</p>
        {!data.facilitiesEnabled && <p className="notice">ميزة المرافق غير متاحة في الإعدادات أو خطة الأكاديمية. يمكنك مراجعة التاريخ وإيقاف الإعداد؛ التفعيل يحتاج إتاحة الميزة.</p>}
        {!detail ? <LoadingState /> : <><div className="sport-policy-grid">{data.branches.map(branch => {const policy = detail.policies.find(x => x.branchId === branch.id); return <form key={`${sport.id}:${branch.id}:${policy?.version ?? 0}:${reload}`} className="sport-policy-card" onSubmit={e => savePolicy(e, branch.id, branch.name, policy?.version ?? 0)}><h3>{branch.name}</h3><label className="checkbox-line"><input name="enabled" type="checkbox" defaultChecked={policy?.enabled ?? false} disabled={locked || (!data.facilitiesEnabled || !sport.isActive) && !policy?.enabled} />تشغيل مرافق السباحة في هذا الفرع</label><label className="checkbox-line"><input name="private" type="checkbox" defaultChecked={policy?.privateTrainingEnabled ?? false} disabled={locked} />تدريب خاص أو شبه خاص</label><label className="checkbox-line"><input name="open" type="checkbox" defaultChecked={policy?.openSwimEnabled ?? false} disabled={locked} />سباحة حرة</label><label className="checkbox-line"><input name="day" type="checkbox" defaultChecked={policy?.dayPassEnabled ?? false} disabled={locked} />زيارة يومية</label><div className="form-actions"><button className="primary-button" disabled={locked}>حفظ إعدادات الفرع</button><Link href={`/dashboard/facilities?branchId=${branch.id}`}>المرافق والمواعيد</Link></div></form>;})}</div><small>تغييرات إعدادات مسجلة: {detail.history.length}</small></>}
      </section>}
    </>}
  </>;
}
