"use client";
import { useEffect, useState } from "react";
import { Button, ErrorState } from "../components/dashboard-shell";
import { csrfRequest } from "./dashboard-api";
import { operationReturnPath } from "../lib/operation-return";
type Context = { membershipId: string; academyId: string; academyName: string; role: string; contextName?: string; allBranches: boolean; allSports: boolean; branchNames: string[]; sportNames: string[] };
const label = (item: Context) => item.contextName || (item.role === "AcademyOwner" ? "مالك الأكاديمية" : "الإدارة الأساسية");
// A full same-origin navigation drops all previous context-scoped React state.
export function WorkContextChooser({ navigate = () => window.location.replace(new URL(operationReturnPath(new URLSearchParams(window.location.search).get("returnTo")) ?? "/dashboard", window.location.origin).toString()) }: { navigate?: () => void }) {
  const [items, setItems] = useState<Context[]>([]); const [selected, setSelected] = useState(""); const [error, setError] = useState(""); const [busy, setBusy] = useState(false);
  useEffect(() => { const controller = new AbortController(); void fetch("/api/v1/my-academies", { cache: "no-store", signal: controller.signal }).then(async r => { if (!r.ok) throw new Error("تعذر تحميل سياقات الدخول؛ سجل الدخول مجددًا."); return r.json(); }).then(data => setItems(data.filter((c: Context) => ["AcademyOwner", "AcademyAdmin"].includes(c.role)))).catch(e => { if (!controller.signal.aborted) setError(e.message); }); return () => controller.abort(); }, []);
  const chosen = items.find(i => i.membershipId === selected);
  async function change() {
    if (!chosen || busy) return; setBusy(true); setError("");
    try { await csrfRequest("/api/v1/session/academy", "POST", { academyId: chosen.academyId, membershipId: chosen.membershipId }); navigate(); }
    catch (e) { setError(e instanceof Error ? e.message : "تعذر تغيير سياق العمل."); setBusy(false); }
  }
  return <section className="access-context-manager"><p>اختر سياقًا واحدًا. سنعيد فتح مساحة العمل ونمسح حالة الصفحات السابقة؛ لا تُجمع الصلاحيات.</p>{error && <ErrorState message={error} />}<fieldset className="context-options"><legend>سياقات العمل المتاحة</legend>{items.map(item => <label className={`detail-card context-option ${selected === item.membershipId ? "selected" : ""}`} key={item.membershipId}><input type="radio" name="context" value={item.membershipId} checked={selected === item.membershipId} onChange={() => setSelected(item.membershipId)} /><span><b>{label(item)}</b><span>{item.academyName}</span><small>{item.allBranches ? "كل الفروع" : item.branchNames.join("، ") || "لم يمنح فرع"} · {item.allSports ? "كل الرياضات" : item.sportNames.join("، ") || "لم تمنح رياضة"}</small></span></label>)}</fieldset><Button disabled={!chosen || busy} onClick={() => void change()}>{busy ? "جارٍ فتح السياق…" : "فتح مساحة العمل"}</Button></section>;
}
