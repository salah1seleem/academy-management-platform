"use client";

import Link from "next/link";
import {useSearchParams} from "next/navigation";
import { FormEvent, useEffect, useState } from "react";
import { ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { csrfRequest } from "./dashboard-api";

type Kind = "General" | "Football" | "Swimming";
type Sport = { id: string; name: string; kind: Kind; isActive: boolean; version: number };
type Policy = { branchId: string; enabled: boolean; privateTrainingEnabled: boolean; openSwimEnabled: boolean; dayPassEnabled: boolean; version: number };
type Data = { sports: Sport[]; branches: { id: string; name: string }[]; facilitiesEnabled: boolean };
type Detail = { policies: Policy[]; history: { branchId: string | null; createdAtUtc: string }[] };
const kinds: Record<Kind, string> = { General: "رياضة عامة", Football: "كرة قدم", Swimming: "سباحة" };

export function SportOperationsSettings() {
  const [data, setData] = useState<Data | null>(null);
  const initialSport=useSearchParams()?.get("sportId")??"";
  const [selected, setSelected] = useState(initialSport);
  const [kind, setKind] = useState<Kind>("General");
  const [detail, setDetail] = useState<Detail | null>(null);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const sport = data?.sports.find(x => x.id === selected);
  async function load() {
    const response = await fetch("/api/v1/manage/sport-settings", { cache: "no-store" });
    if (!response.ok) throw new Error("خصائص الرياضات متاحة للمالك فقط. تعذر تحميل الإعدادات.");
    const value: Data = await response.json(); setData(value); return value;
  }
  useEffect(() => {
    const controller = new AbortController();
    fetch("/api/v1/manage/sport-settings", { cache: "no-store", signal: controller.signal })
      .then(async response => { if (!response.ok) throw new Error("خصائص الرياضات متاحة للمالك فقط. تعذر تحميل الإعدادات."); return response.json(); }).then(value=>{setData(value);setKind(value.sports.find((s:Sport)=>s.id===initialSport)?.kind??"General");})
      .catch(e => { if (!controller.signal.aborted) setError(e.message); });
    return () => controller.abort();
  }, [initialSport]);
  useEffect(() => {
    if (sport?.kind !== "Swimming") return;
    const controller = new AbortController();
    fetch(`/api/v1/manage/sport-settings/${sport.id}/swimming`, { signal: controller.signal, cache: "no-store" })
      .then(async r => { if (!r.ok) throw new Error(); return r.json(); }).then(setDetail)
      .catch(() => { if (!controller.signal.aborted) setError("تعذر تحميل إعدادات السباحة. حاول مرة أخرى."); });
    return () => controller.abort();
  }, [sport?.id, sport?.kind]);
  function select(id: string) { setSelected(id); setKind(data?.sports.find(x => x.id === id)?.kind ?? "General"); setDetail(null); setError(""); setNotice(""); }
  async function saveKind(e: FormEvent<HTMLFormElement>) {
    e.preventDefault(); if (!sport || busy) return; setBusy(true); setError(""); setNotice("");
    try { await csrfRequest(`/api/v1/manage/sport-settings/${sport.id}/kind`, "PUT", { kind, version: sport.version }); await load(); setNotice("تم حفظ نوع الرياضة."); }
    catch (err) { setError(err instanceof Error ? err.message : "تعذر حفظ نوع الرياضة."); }
    finally { setBusy(false); }
  }
  async function savePolicy(e: FormEvent<HTMLFormElement>, branchId: string, version: number) {
    e.preventDefault(); if (!sport || busy) return; const fields = new FormData(e.currentTarget); setBusy(true); setError(""); setNotice("");
    try {
      await csrfRequest(`/api/v1/manage/sport-settings/${sport.id}/swimming/${branchId}`, "PUT", { enabled: fields.has("enabled"), privateTrainingEnabled: fields.has("private"), openSwimEnabled: fields.has("open"), dayPassEnabled: fields.has("day"), version });
      const response = await fetch(`/api/v1/manage/sport-settings/${sport.id}/swimming`, { cache: "no-store" });
      if (!response.ok) throw new Error("تم الحفظ؛ تعذر تحديث القائمة. أعد فتح الصفحة.");
      setDetail(await response.json()); setNotice(`تم حفظ إعدادات ${data?.branches.find(b => b.id === branchId)?.name}.`);
    } catch (err) { setError(err instanceof Error ? err.message : "تعذر حفظ إعدادات الفرع."); }
    finally { setBusy(false); }
  }
  return <>
    <PageHeader title="خصائص الرياضات" context="الإعدادات / تشغيل الرياضات" description="اضبط الرياضة مرة واحدة، ثم خدمات كل فرع على حدة." />
    <Link className="secondary-button" href="/dashboard/settings">العودة إلى الإعدادات</Link>
    {error && <ErrorState message={error} />}{notice && <p className="success-message" role="status">{notice}</p>}
    {!data ? !error && <LoadingState /> : <>
      <section className="detail-card sport-settings-picker"><label>الرياضة<select disabled={busy} value={selected} onChange={e => select(e.target.value)}><option value="">اختر الرياضة</option>{data.sports.map(x => <option key={x.id} value={x.id}>{x.name}{x.isActive ? "" : " — موقوفة"}</option>)}</select></label>
        {sport && <form className="sport-kind-form" onSubmit={e => void saveKind(e)}><label>نوع الرياضة<select disabled={busy} value={kind} onChange={e => setKind(e.target.value as Kind)}>{Object.entries(kinds).map(([key, label]) => <option value={key} key={key}>{label}</option>)}</select></label><p className="muted">يحدد إعدادات التشغيل، ولا يغيّر الاسم أو الباقات. النوع المرتبط بمرفق أو إعداد محفوظ لا يُبدّل.</p><button className="primary-button" disabled={busy || kind === sport.kind}>حفظ نوع الرياضة</button></form>}
      </section>
      {sport&&sport.kind!=="Swimming"&&data.facilitiesEnabled&&<section className="detail-card"><h2>مرافق الرياضة</h2><Link href="/dashboard/facilities">الملاعب والمناطق والحجوزات</Link></section>}
      {sport?.kind === "Swimming" && <section className="detail-card" aria-label="إعدادات السباحة"><h2>إعدادات السباحة حسب الفرع</h2><p className="muted">إيقاف الفرع يمنع عمليات سباحة جديدة ويحفظ سجلاته. الأسعار والنسب في الاتفاقات المالية؛ اعتماد السعة لكل مورد في المرافق.</p>
        {!data.facilitiesEnabled && <p className="notice">ميزة المرافق غير متاحة في الإعدادات أو خطة الأكاديمية. يمكنك مراجعة التاريخ وإيقاف الإعداد؛ التفعيل يحتاج إتاحة الميزة.</p>}
        {!detail ? <LoadingState /> : <><div className="sport-policy-grid">{data.branches.map(branch => { const policy = detail.policies.find(x => x.branchId === branch.id); return <form key={`${branch.id}:${policy?.version ?? 0}`} className="sport-policy-card" onSubmit={e => void savePolicy(e, branch.id, policy?.version ?? 0)}><h3>{branch.name}</h3><label className="checkbox-line"><input name="enabled" type="checkbox" defaultChecked={policy?.enabled ?? false} disabled={(!data.facilitiesEnabled || !sport.isActive) && !policy?.enabled} />تشغيل مرافق السباحة في هذا الفرع</label><label className="checkbox-line"><input name="private" type="checkbox" defaultChecked={policy?.privateTrainingEnabled ?? false} />تدريب خاص أو شبه خاص</label><label className="checkbox-line"><input name="open" type="checkbox" defaultChecked={policy?.openSwimEnabled ?? false} />سباحة حرة</label><label className="checkbox-line"><input name="day" type="checkbox" defaultChecked={policy?.dayPassEnabled ?? false} />زيارة يومية</label><div className="form-actions"><button className="primary-button" disabled={busy}>حفظ إعدادات الفرع</button><Link href={`/dashboard/facilities?branchId=${branch.id}`}>المرافق والمواعيد</Link></div></form>; })}</div><small>تغييرات إعدادات مسجلة: {detail.history.length}</small></>}
      </section>}
    </>}
  </>;
}
