"use client";
import { useEffect, useState } from "react";
import { ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { csrfRequest } from "./dashboard-api";
import { TransferSettings } from "./manual-payments";
type Brand = { displayName: string; coachAttendanceEnabled: boolean; currency?:string };
export function AcademySettings() {
  const [brand, setBrand] = useState<Brand | null>(null);
  const [canManage, setCanManage] = useState(false);
  const [message, setMessage] = useState("");
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    void fetch("/api/v1/academy/experience").then(r => r.ok ? r.json() : Promise.reject()).then(setBrand).catch(() => setMessage("تعذر تحميل إعدادات الأكاديمية."));
    void fetch("/api/v1/my-permissions").then(r => r.ok ? r.json() : []).then(x => setCanManage(Array.isArray(x) && x.includes("academy.settings.manage")));
  }, []);
  async function policy(value: boolean) {
    setBusy(true);
    try { await csrfRequest("/api/v1/manage/academy/experience", "PUT", { coachAttendanceEnabled: value }); setBrand(x => x && { ...x, coachAttendanceEnabled: value }); setMessage("تم حفظ سياسة حضور المدرب. تُطبق على الخادم والموبايل."); }
    catch { setMessage("تعذر حفظ سياسة الحضور."); } finally { setBusy(false); }
  }
  return <><PageHeader title="إعدادات الأكاديمية" context="الهوية وتجربة الأكاديمية" />{message && <p role="status">{message}</p>}{!brand ? <LoadingState /> : <>
    <section className="detail-card"><h2>{brand.displayName}</h2>{canManage ? <label className="policy-switch"><input type="checkbox" checked={brand.coachAttendanceEnabled} disabled={busy} onChange={e => void policy(e.target.checked)} />السماح للمدرب بتسجيل حضور اللاعبين من التطبيق</label> : <p>إعداد سياسة الحضور للمالك أو الموظف المصرح له فقط.</p>}<p className="muted">إيقاف الخيار لا يمنع التقييمات أو عرض المواعيد؛ يمنع تسجيل الحضور على الخادم.</p></section>
    <TransferSettings expanded />
    <section className="detail-card"><h2>عملة الأكاديمية</h2><p>{brand.currency?({EGP:"الجنيه المصري · EGP",SAR:"الريال السعودي · SAR",AED:"الدرهم الإماراتي · AED",USD:"الدولار الأمريكي · USD"} as Record<string,string>)[brand.currency]??brand.currency:"تعذر قراءة العملة الحالية؛ لا نفترض عملة بديلة."}</p><small>تختار عند إنشاء الأكاديمية وتستخدم في الباقات والرواتب والمصروفات والتقارير. لا تحويل تلقائي أو إعادة كتابة للسجلات المالية السابقة من إعدادات التشغيل.</small></section>
    <section className="detail-card"><h2>هوية الأكاديمية</h2><p>الاسم والشعار والألوان يديرها مشغّل المنصة من حساب مستقل، للحفاظ على هوية كل أكاديمية وعزلها.</p></section>
  </>}{!brand && message && <ErrorState message={message} />}</>;
}
