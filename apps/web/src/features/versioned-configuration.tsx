"use client";
import { useCallback, useEffect, useState } from "react";
import { ConfirmationDialog, ErrorState, LoadingState } from "../components/dashboard-shell";
import { csrfRequest } from "./dashboard-api";
type Settings = { currency: string; timeZone: string; features: Record<string, boolean>; cashEnabled: boolean; consecutiveAbsenceAlert: number; tenantMarketingOptIn: boolean };
type Version = { id: string; revision: number; version: number; state: string; contentHash: string; settings: Settings };
type Data = { entitlements: { feature: string; enabled: boolean; planCode: string }[]; versions: Version[]; history: { configurationId: string; from: string; to: string; createdAtUtc: string }[] };
const states: Record<string, string> = { Draft: "مسودة", Validated: "متحقق منها", Staged: "جاهزة للتفعيل", Active: "سارية", Suspended: "موقوفة", Superseded: "إصدار سابق" };
const labels: Record<string, string> = { training: "التدريب", nutrition: "التغذية", medical: "السجلات الطبية", media: "الوسائط", catalog: "المنتجات الرياضية", news: "الأخبار", matches: "المباريات", payroll: "الرواتب", facilities: "المرافق والحارات", expenses: "المصروفات", external_partners: "الشركاء الخارجيون", learning_hub: "مركز التعلم", calendar: "التقويم", crm: "متابعة العملاء", simulator: "المحاكي", community_ads: "إعلانات المجتمع", voice_notes: "الملاحظات الصوتية", ai_assistant: "المساعد الذكي" };
export const featureLabels = labels;
const next: Record<string, string> = { Draft: "Validated", Validated: "Staged", Staged: "Active", Active: "Suspended", Suspended: "Staged" };
const actions: Record<string, string> = { Draft: "التحقق", Validated: "تجهيز التفعيل", Staged: "تفعيل الآن", Active: "إيقاف الإصدار", Suspended: "إعادة التجهيز" };
export function VersionedConfiguration() {
  const [confirm, setConfirm] = useState<Version | null>(null);
  const [data, setData] = useState<Data | null>(null), [draft, setDraft] = useState<Settings | null>(null), [error, setError] = useState(""), [busy, setBusy] = useState(false), [allowed, setAllowed] = useState(false);
  const load = useCallback(async () => { const r = await fetch("/api/v1/manage/configuration"); if (!r.ok) throw new Error("تعذر تحميل إصدارات الإعدادات."); const d = await r.json() as Data; setData(d); setDraft(d.versions.find(v => v.state === "Active")?.settings ?? d.versions[0]?.settings ?? null); }, []);
  useEffect(() => { void fetch("/api/v1/my-permissions").then(r => r.ok ? r.json() : []).then(p => { if (Array.isArray(p) && p.includes("academy.settings.manage")) { setAllowed(true); return load(); } }).catch(() => setError("تعذر تحميل إعدادات التشغيل.")); }, [load]);
  async function act(action: () => Promise<unknown>) { if (busy) return; setBusy(true); setError(""); try { await action(); await load(); } catch (e) { setError(e instanceof Error ? e.message : "تعذر حفظ الإعدادات."); } finally { setBusy(false); } }
  async function transition(v: Version) { await act(() => csrfRequest(`/api/v1/manage/configuration/${v.id}/transition`, "POST", { target: next[v.state], version: v.version })); setConfirm(null); }
  function differences(v: Version) {
    const active = data?.versions.find(x => x.state === "Active")?.settings;
    if (!active) return "لا يوجد إصدار سارٍ؛ سيطبق هذا الإصدار بعد الاعتماد.";
    const changed = Object.keys(v.settings.features).filter(f => active.features[f] !== v.settings.features[f]).map(f => `${labels[f] ?? f}: ${v.settings.features[f] ? "تفعيل" : "إيقاف"}`);
    if (active.consecutiveAbsenceAlert !== v.settings.consecutiveAbsenceAlert) changed.push(`تنبيه الغياب: ${active.consecutiveAbsenceAlert} ← ${v.settings.consecutiveAbsenceAlert}`);
    return changed.join(" · ") || "لا اختلاف في الميزات أو تنبيه الغياب عن الإصدار الساري.";
  }
  if (!allowed) return null;
  return <section className="detail-card"><h2>إصدارات إعدادات التشغيل</h2><p>المسودة لا تغيّر التشغيل. تحقق منها ثم جهّزها واعتمد التفعيل. الخطة المتاحة تُدار من المنصة ولا يمنح تفعيل زر صلاحية للمستخدم.</p>{error && <ErrorState message={error} />}{!data ? <LoadingState /> : <>
    {draft && <form className="form-shell" onSubmit={e => { e.preventDefault(); void act(() => csrfRequest("/api/v1/manage/configuration/drafts", "POST", draft)); }}><label>المنطقة الزمنية<input value={draft.timeZone} onChange={e => setDraft({ ...draft, timeZone: e.target.value })} required /></label><label>تنبيه الغياب المتتالي<input type="number" min={1} max={30} value={draft.consecutiveAbsenceAlert} onChange={e => setDraft({ ...draft, consecutiveAbsenceAlert: Number(e.target.value) })} /></label><fieldset><legend>الميزات التشغيلية</legend>{data.entitlements.map(e => <label className="policy-switch" key={e.feature}><input type="checkbox" disabled={!e.enabled} checked={draft.features[e.feature] ?? false} onChange={ev => setDraft({ ...draft, features: { ...draft.features, [e.feature]: ev.target.checked } })} />{labels[e.feature] ?? e.feature}{!e.enabled && <small>غير متاحة في الخطة</small>}</label>)}</fieldset><button className="primary-button" disabled={busy}>حفظ إصدار مسودة</button></form>}
    <div className="linked-list">{data.versions.map(v => <article key={v.id}><div><strong>الإصدار {v.revision} · {states[v.state]}</strong><small dir="ltr">{v.contentHash.slice(0, 12)}</small>{v.state !== "Active" && <p>{differences(v)}</p>}</div>{next[v.state] && <button disabled={busy} onClick={() => v.state === "Staged" || v.state === "Active" ? setConfirm(v) : void transition(v)}>{actions[v.state]}</button>}</article>)}</div>
    {confirm && <ConfirmationDialog open title={confirm.state === "Active" ? "إيقاف إعدادات التشغيل؟" : "تفعيل إصدار الإعدادات؟"} message={confirm.state === "Active" ? "ستتعطل الميزات التشغيلية حتى تفعيل إصدار صالح. لا تُحذف السجلات." : differences(confirm)} confirmLabel="تأكيد الاعتماد" busy={busy} onConfirm={() => void transition(confirm)} onCancel={() => setConfirm(null)} />}
    <details><summary>سجل تغير الحالات ({data.history.length})</summary>{data.history.map((h, i) => <p key={i}>{states[h.from]} ← {states[h.to]} · {new Date(h.createdAtUtc).toLocaleString("ar-EG-u-nu-latn")}</p>)}</details>
  </>}</section>;
}
