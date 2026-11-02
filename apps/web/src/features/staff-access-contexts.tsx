"use client";
import { FormEvent, useCallback, useEffect, useState } from "react";
import { Button, ConfirmationDialog, ErrorState } from "../components/dashboard-shell";
import { csrfRequest } from "./dashboard-api";

type Context = { id: string; name: string; isActive: boolean; revision: number; allBranches: boolean; allSports: boolean; branches: string[]; sports: string[] };
export function StaffAccessContexts({ id, name, onConfigure, onClose }: { id: string; name: string; onConfigure: (id: string, name: string) => void; onClose: () => void }) {
  const [items, setItems] = useState<Context[]>([]); const [error, setError] = useState(""); const [busy, setBusy] = useState(false);
  const [draft, setDraft] = useState(""); const [edit, setEdit] = useState<Context | null>(null); const [confirm, setConfirm] = useState(false);
  const load = useCallback(async () => {
    const response = await fetch(`/api/v1/manage/staff/${id}/contexts`, { cache: "no-store" });
    if (!response.ok) throw new Error("تعذر عرض سياقات هذا الإداري؛ إدارة السياقات للمالك فقط.");
    const data = await response.json(); setItems(data.items);
  }, [id]);
  useEffect(() => { const controller = new AbortController(); void fetch(`/api/v1/manage/staff/${id}/contexts`, { cache: "no-store", signal: controller.signal }).then(async response => { if (!response.ok) throw new Error("تعذر عرض سياقات هذا الإداري؛ إدارة السياقات للمالك فقط."); return response.json(); }).then(data => setItems(data.items)).catch(e => { if (!controller.signal.aborted) setError(e.message); }); return () => controller.abort(); }, [id]);
  async function create(event: FormEvent) {
    event.preventDefault(); if (busy) return; setBusy(true); setError("");
    try {
      const response = await csrfRequest(`/api/v1/manage/staff/${id}/contexts`, "POST", { name: draft });
      const saved = await response.json(); onConfigure(saved.id, `${name} · ${draft}`);
    } catch (e) { setError(e instanceof Error ? e.message : "تعذر إنشاء السياق."); } finally { setBusy(false); }
  }
  async function saveState() {
    if (!edit || busy) return; setBusy(true); setError("");
    try { await csrfRequest(`/api/v1/manage/staff/${edit.id}/context`, "PUT", { name: draft, isActive: edit.isActive, revision: edit.revision }); setEdit(null); setConfirm(false); setDraft(""); await load(); }
    catch (e) { setError(e instanceof Error ? e.message : "تعذر حفظ السياق."); setConfirm(false); }
    finally { setBusy(false); }
  }
  return <section className="access-context-manager" aria-label={`سياقات ${name}`}>
    <div className="form-actions"><Button variant="secondary" onClick={onClose}>عودة للموظفين</Button></div>
    <div className="detail-card"><h2>سياقات عمل {name}</h2><p>نفس حساب الدخول، وصلاحيات منفصلة لكل سياق. لا تجمع صلاحيات سياق قوي في فرع مع صلاحيات سياق آخر.</p><p>السياقات الإضافية للتشغيل فقط حاليًا؛ الرواتب وإدارة المستخدمين والماليات المشتركة تحتاج المسار الأوسع. لا يُنشأ موظف أو راتب جديد.</p></div>
    {error && <ErrorState message={error} />}
    <div className="role-template-grid">{items.map(item => <article className="detail-card" key={item.id}><h3>{item.name}</h3><p>{item.isActive ? "فعال" : "موقوف"} · إصدار {item.revision}</p><p>{item.allBranches ? "كل الفروع" : `${item.branches.length} فروع محددة`} · {item.allSports ? "كل الرياضات" : `${item.sports.length} رياضات محددة`}</p><div className="form-actions"><Button onClick={() => onConfigure(item.id, `${name} · ${item.name}`)}>الصلاحيات والنطاق</Button><Button variant="secondary" onClick={() => { setEdit(item); setDraft(item.name); setConfirm(false); }}>الاسم والتفعيل</Button></div></article>)}</div>
    {edit ? <form className="form-shell" onSubmit={e => { e.preventDefault(); setConfirm(true); }}><h3>مراجعة حالة السياق</h3><label>اسم السياق<input value={draft} onChange={e => setDraft(e.target.value)} required maxLength={80} /></label><label className="checkbox-line"><input type="checkbox" checked={edit.isActive} onChange={e => setEdit({ ...edit, isActive: e.target.checked })} /> سياق فعال</label><p>الإيقاف يمنع استخدام هذا السياق؛ بقية سياقات الشخص وعضوياته الأخرى لا تتغير.</p><div className="form-actions"><Button type="submit" disabled={busy}>مراجعة الحفظ</Button><Button variant="secondary" onClick={() => { setEdit(null); setDraft(""); }}>إلغاء</Button></div><ConfirmationDialog open={confirm} title="تأكيد حالة السياق" message={`${draft}: ${edit.isActive ? "تفعيل بعد تحقق النطاق والصلاحيات" : "إيقاف وصول الجلسات بهذا السياق"}`} confirmLabel="تأكيد حالة السياق" busy={busy} onCancel={() => setConfirm(false)} onConfirm={() => void saveState()} /></form>
      : <form className="form-shell" onSubmit={e => void create(e)}><h3>إضافة سياق عمل</h3><label>اسم السياق الجديد<input value={draft} onChange={e => setDraft(e.target.value)} required maxLength={80} placeholder="مثل منسق التدريب — فرع التجمع" /></label><p>يبدأ موقوفًا وبلا صلاحيات. حدد قالبًا وفروعًا ورياضات، ثم راجع تفعيله صراحة.</p><Button type="submit" disabled={busy}>إنشاء وتحديد الصلاحيات</Button></form>}
  </section>;
}
