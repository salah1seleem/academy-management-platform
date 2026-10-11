"use client";
import { FormEvent, useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { ErrorState, PageHeader } from "../../../../components/dashboard-shell";
import { csrfRequest, uploadAsset } from "../../../../features/dashboard-api";
import { formatMoneyAr } from "../../../../lib/formatters";
type Item = { id: string; arabicName: string; branchId?: string; sportId?: string; ageCategoryId?: string };
type Plan = Item & { price: number; currency: string; isActive: boolean; serviceKind?: string; allBranches?: boolean; branchIds?: string[] };
type Options = { branches: Item[]; sports: Item[]; categories: Item[]; groups: Item[] };
type Quote = { originalAmount: number; discountAmount: number; finalAmount: number; discountReason?: string; currency: string };
export default function NewPlayerPage() {
  const router = useRouter(); const key = useRef("");
  const [options, setOptions] = useState<Options>({ branches: [], sports: [], categories: [], groups: [] });
  const [plans, setPlans] = useState<Plan[]>([]); const [branchId, setBranch] = useState(""); const [sportId, setSport] = useState(""); const [categoryId, setCategory] = useState(""); const [planId, setPlan] = useState(""); const [error, setError] = useState(""); const [busy, setBusy] = useState(false);
  const [phone, setPhone] = useState(""); const [ruleId, setRule] = useState(""); const [rules, setRules] = useState<Item[]>([]); const [quote, setQuote] = useState<Quote | null>(null);
  useEffect(() => { fetch("/api/v1/discount-rules/").then(async r => { if (r.ok) { const d = await r.json(); setRules(d.items.filter((x: { isActive: boolean; isSiblingOffer: boolean }) => x.isActive && !x.isSiblingOffer)); } }); }, []);
  useEffect(() => { Promise.all([fetch("/api/v1/manage/structure/options"), fetch("/api/v1/subscriptions/plans")]).then(async ([o, p]) => { if (!o.ok || !p.ok) throw new Error(); setOptions(await o.json()); setPlans(await p.json()); }).catch(() => setError("تعذر تحميل المجموعات والباقات.")); }, []);
  const groups = options.groups.filter(g => g.branchId === branchId && g.sportId === sportId && g.ageCategoryId === categoryId);
  const availablePlans = plans.filter(p => p.isActive && (!p.serviceKind || p.serviceKind === "Training") && (!sportId || p.sportId === sportId) && (!branchId || p.allBranches !== false || p.branchIds?.includes(branchId)));
  const selectedPlan = availablePlans.find(p => p.id === planId && p.sportId === sportId);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (busy || !selectedPlan || !quote) return;
    const data = new FormData(event.currentTarget); setBusy(true); setError(""); key.current ||= crypto.randomUUID();
    try {
      const response = await csrfRequest("/api/v1/people/registrations", "POST", { playerArabicName: data.get("playerName"), dateOfBirth: data.get("birth"), gender: Number(data.get("gender")), guardianName: data.get("guardianName"), guardianPhone: phone, branchId, sportId, groupId: data.get("groupId"), planId, paymentMethod: data.get("paymentMethod"), discountRuleId: ruleId || null, expectedAmount: quote.finalAmount }, { "Idempotency-Key": key.current });
      const result = await response.json() as { playerId: string };
      const photo = data.get("photo");
      if (photo instanceof File && photo.size) {
        try { await uploadAsset(photo, "Photo", { playerId: result.playerId }); }
        catch { setError("تم التسجيل والتحصيل بنجاح، لكن الصورة لم تُرفع. يمكنك رفعها من تعديل ملف اللاعب."); return; }
      }
      router.push("/dashboard/players");
    } catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر حفظ التسجيل."); }
    finally { setBusy(false); }
  }
  return <><PageHeader title="تسجيل لاعب جديد" context="اللاعبون" /><form className="form-shell" onSubmit={e => void submit(e)}>
    <h2>بيانات اللاعب</h2><label>اسم اللاعب بالعربية<input name="playerName" required /></label><label>تاريخ الميلاد<input name="birth" type="date" required /></label><label>النوع<select name="gender" required><option value="1">ذكر</option><option value="2">أنثى</option></select></label>
    <label>صورة اللاعب (اختياري)<input name="photo" type="file" accept="image/jpeg,image/png,image/webp" /><small>صورة حتى 8 ميجابايت، تظهر للأشخاص المصرح لهم فقط.</small></label>
    <h2>ولي الأمر</h2><label>اسم ولي الأمر<input name="guardianName" required /></label><label>رقم الهاتف<input name="phone" dir="ltr" required value={phone} onChange={e => { setPhone(e.target.value); setQuote(null); }} /></label>
    <h2>المجموعة والباقة</h2><label>الفرع<select value={branchId} onChange={e => { setBranch(e.target.value); setPlan(""); setQuote(null); }} required><option value="">اختر الفرع</option>{options.branches.map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select></label>
    <label>الرياضة<select value={sportId} onChange={e => { setSport(e.target.value); setPlan(""); setQuote(null); }} required><option value="">اختر الرياضة</option>{options.sports.map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select></label>
    <label>الفئة العمرية<select value={categoryId} onChange={e => setCategory(e.target.value)} required><option value="">اختر الفئة</option>{options.categories.map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select></label>
    <label>المجموعة<select name="groupId" key={`${branchId}-${sportId}-${categoryId}`} required><option value="">اختر المجموعة</option>{groups.map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select></label>
    <label>الباقة<select aria-label="الباقة" value={planId} onChange={e => { const plan = availablePlans.find(p => p.id === e.target.value); setPlan(e.target.value); if (plan?.sportId) setSport(plan.sportId); setQuote(null); }} required><option value="">اختر الباقة</option>{availablePlans.map(p => <option key={p.id} value={p.id}>{p.arabicName} · {options.sports.find(s => s.id === p.sportId)?.arabicName} · {formatMoneyAr(p.price, p.currency)}</option>)}</select>{sportId && availablePlans.length === 0 && <small>لا توجد باقات نشطة لهذه الرياضة. أضف أو فعّل باقة من قسم الباقات.</small>}</label>
    <label>عرض دائم للاعب<select value={ruleId} onChange={e => { setRule(e.target.value); setQuote(null); }}><option value="">لا يوجد عرض إضافي</option>{rules.map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select><small>عرض الإخوة يُحتسب تلقائيًا عند وجود طفل آخر باشتراك مدفوع وفعال. لا تُجمع الخصومات.</small></label>
    <label>طريقة التحصيل<select name="paymentMethod"><option value="Cash">نقدي</option><option value="Online">تحويل إلكتروني تم استلامه</option></select></label>
    <p className="demo-note">التأكيد يسجل التحصيل الفعلي ويُفعّل الاشتراك ويُصدر إيصالًا. لا تؤكد قبل استلام المبلغ.</p>
    <button type="button" disabled={!selectedPlan || !branchId || !phone || busy} onClick={async () => { try { const r = await csrfRequest("/api/v1/people/registration-quote", "POST", { planId, branchId, guardianPhone: phone, discountRuleId: ruleId || null }); setQuote(await r.json()); } catch { setError("تعذر حساب المبلغ المستحق."); } }}>حساب المبلغ المستحق</button>
    {quote && <div className="adjustment-preview"><span>السعر الأصلي: {formatMoneyAr(quote.originalAmount, quote.currency)}</span><span>الخصم: {formatMoneyAr(quote.discountAmount, quote.currency)} · {quote.discountReason}</span><b>المبلغ المطلوب تحصيله: {formatMoneyAr(quote.finalAmount, quote.currency)}</b></div>}{error && <ErrorState message={error} />}
    <div className="form-actions"><button type="submit" className="primary-button" disabled={busy || !selectedPlan || !quote}>{busy ? "جارٍ التسجيل…" : "تسجيل وتأكيد التحصيل"}</button><button type="button" onClick={() => router.back()}>إلغاء</button></div>
  </form></>;
}
