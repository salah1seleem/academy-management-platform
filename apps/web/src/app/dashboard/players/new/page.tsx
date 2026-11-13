"use client";
import Link from "next/link";
import {FilePicker} from "../../../../components/file-picker";
import { FormEvent, useEffect, useRef, useState } from "react";
import { useRouter } from "next/navigation";
import { ErrorState, LoadingState, PageHeader } from "../../../../components/dashboard-shell";
import { announceSaveSuccess, csrfRequest, DashboardRequestError, uploadAsset } from "../../../../features/dashboard-api";
import { formatMoneyAr } from "../../../../lib/formatters";
import { registrationMatchesProduct } from "../../../../features/group-training-format";
import { RegistrationCashShift } from "../../../../features/registration-cash-shift";
type Item = { id: string; arabicName: string; branchId?: string; sportId?: string; ageCategoryId?: string; registrationFormat?: string; trainingLevelId?: string; trainingLevel?: string };
type Plan = Item & { price: number; currency: string; isActive: boolean; serviceKind?: string; trainingFormat?: string; allBranches?: boolean; branchIds?: string[] };
type Options = { branches: Item[]; sports: Item[]; categories: Item[]; groups: Item[] };
type Quote = { originalAmount: string | number; discountAmount: string | number; finalAmount: string | number; discountReason?: string; currency: string };
export default function NewPlayerPage() {
  const router = useRouter(); const key = useRef("");
  const [options, setOptions] = useState<Options>();
  const [setupError, setSetupError] = useState(false), [setupRevision, setSetupRevision] = useState(0);
  const [plans, setPlans] = useState<Plan[]>([]); const [branchId, setBranch] = useState(""); const [sportId, setSport] = useState(""); const [categoryId, setCategory] = useState(""); const [planId, setPlan] = useState(""); const [error, setError] = useState(""); const [busy, setBusy] = useState(false);
  const [phone, setPhone] = useState(""); const [ruleId, setRule] = useState(""); const [rules, setRules] = useState<Item[]>([]); const [quote, setQuote] = useState<Quote | null>(null);
  const quoteRequest = useRef(0);
  const [quoteBusy, setQuoteBusy] = useState(false);
  const [quoteError, setQuoteError] = useState("");
  const [photoIssue, setPhotoIssue] = useState<{ playerId: string; waived: boolean }>();
  const [groupId, setGroup] = useState("");
  const [allowExceptional,setAllowExceptional]=useState(false),[exceptional,setExceptional]=useState(false),[exceptionType,setExceptionType]=useState(2),[exceptionValue,setExceptionValue]=useState(''),[exceptionReason,setExceptionReason]=useState(''),[replaceDiscount,setReplaceDiscount]=useState(false);
  const exceptionalDiscount=exceptional?{type:exceptionType,value:exceptionValue,reason:exceptionReason,replaceExistingDiscount:replaceDiscount}:null;
  useEffect(()=>{const c=new AbortController();fetch('/api/v1/me',{signal:c.signal}).then(r=>r.ok?r.json():null).then(async me=>{if(me?.role==='AcademyOwner'){setAllowExceptional(true);return;}const r=await fetch('/api/v1/my-access',{signal:c.signal});if(!r.ok)return;const d=await r.json();if(!c.signal.aborted)setAllowExceptional(d.workflowActions?.some((a:{key:string;allowed:boolean})=>a.key==='workflow.subscriptions.exceptional-discount'&&a.allowed===true)===true);}).catch(()=>{});return()=>c.abort();},[]);

  const [method, setMethod] = useState(""), [cashSessionId, setCashSession] = useState("");
  useEffect(() => {
    const controller = new AbortController();
    fetch("/api/v1/discount-rules/", { signal: controller.signal }).then(async response => {
      if (!response.ok) return;
      const data = await response.json();
      if (!controller.signal.aborted && Array.isArray(data?.items)) {
        setRules(data.items.filter((rule: { isActive: boolean; isSiblingOffer: boolean }) => rule.isActive && !rule.isSiblingOffer));
      }
    }).catch(() => undefined);
    return () => controller.abort();
  }, []);
  useEffect(() => {
    const controller = new AbortController();
    async function load(path: string) {
      const response = await fetch(path, { signal: controller.signal, cache: "no-store" });
      if (!response.ok) throw new Error();
      return response.json();
    }
    Promise.all([load("/api/v1/manage/structure/options"), load("/api/v1/subscriptions/plans")]).then(([loadedOptions, loadedPlans]) => {
      if (!Array.isArray(loadedOptions?.branches) || !Array.isArray(loadedOptions.sports) || !Array.isArray(loadedOptions.categories) || !Array.isArray(loadedOptions.groups) || !Array.isArray(loadedPlans)) throw new Error();
      if (controller.signal.aborted) return;
      setOptions(loadedOptions);
      setPlans(loadedPlans);
    }).catch(() => { if (!controller.signal.aborted) setSetupError(true); });
    return () => controller.abort();
  }, [setupRevision]);

  const heading = <PageHeader title="تسجيل لاعب جديد" context="اللاعبون" />;
  if (!options) return <>{heading}{setupError ? <>
    <ErrorState message="تعذر تحميل الفروع والمجموعات والباقات." />
    <button className="secondary-button" type="button" onClick={() => { setSetupError(false); setSetupRevision(value => value + 1); }}>إعادة تحميل النموذج</button>
  </> : <LoadingState message="جارٍ تحميل خيارات التسجيل…" />}</>;
  if (photoIssue) return <>{heading}<section className="detail-card">
    <p role="status">{photoIssue.waived ? "تم تسجيل اللاعب واعتماد الإعفاء." : "تم تسجيل اللاعب وتأكيد التحصيل."}</p>
    <ErrorState message="تعذر رفع الصورة." />
    <Link className="button button-primary" href={`/dashboard/players/${photoIssue.playerId}/edit`}>إضافة الصورة إلى ملف اللاعب</Link>
  </section></>;

  const product = plans.find(p => p.id === planId); const group = options.groups.find(g => g.id === groupId);
  const groups = options.groups.filter(g => g.branchId === branchId && g.sportId === sportId && g.ageCategoryId === categoryId && (!product || registrationMatchesProduct(g.registrationFormat, product.trainingFormat)));
  const availablePlans = plans.filter(p => p.isActive && (!p.serviceKind || p.serviceKind === "Training") && (!sportId || p.sportId === sportId) && (!branchId || p.allBranches !== false || p.branchIds?.includes(branchId)) && (!group || registrationMatchesProduct(group.registrationFormat, p.trainingFormat)));
  const selectedPlan = availablePlans.find(p => p.id === planId && p.sportId === sportId);
  const requiresGroup = !registrationMatchesProduct("Group", selectedPlan?.trainingFormat);
  const waived = quote !== null && Number(quote.finalAmount) === 0;
  const canQuote = Boolean(selectedPlan && branchId && phone.trim() && (!requiresGroup || groupId) && !busy && !quoteBusy);
  const canRegister = Boolean(!busy && selectedPlan && quote && (!requiresGroup || groupId) && (waived || method && (method !== "Cash" || cashSessionId)));
  function clearQuote() {
    quoteRequest.current += 1;
    setQuote(null);
    setQuoteBusy(false);
    setQuoteError("");
    setCashSession("");
  }
  async function requestQuote() {
    if (!canQuote) return;
    const request = ++quoteRequest.current;
    setQuote(null);
    setQuoteError("");
    setQuoteBusy(true);
    setCashSession("");
    try {
      const response = await csrfRequest("/api/v1/people/registration-quote", "POST", { planId, branchId, guardianPhone: phone, discountRuleId: ruleId || null, exceptionalDiscount });
      const next = await response.json() as Quote;
      if (typeof next?.currency !== "string" || [next.originalAmount, next.discountAmount, next.finalAmount].some(value => (typeof value !== "number" && typeof value !== "string") || !String(value).trim() || !Number.isFinite(Number(value)) || Number(value) < 0)) throw new Error();
      if (request === quoteRequest.current) setQuote(next);
    } catch (caught) {
      if (request === quoteRequest.current) setQuoteError(caught instanceof DashboardRequestError && caught.status < 500 ? caught.message : "تعذر حساب المبلغ المستحق. أعد المحاولة.");
    } finally {
      if (request === quoteRequest.current) setQuoteBusy(false);
    }
  }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!canRegister || !quote) return;
    if (!window.confirm(waived?"اعتماد إعفاء كامل وتسجيل اللاعب دون تحصيل أو إيصال مالي؟":`تأكيد استلام ${formatMoneyAr(quote.finalAmount, quote.currency)} ${method==="Cash"?"نقدًا في ورديتك":"بالتحويل الإلكتروني"} وتسجيل اللاعب وإصدار الإيصال؟`)) return;
    const data = new FormData(event.currentTarget); setBusy(true); setError(""); key.current ||= crypto.randomUUID();
    try {
      const response = await csrfRequest("/api/v1/people/registrations", "POST", { playerArabicName: data.get("playerName"), dateOfBirth: data.get("birth"), gender: Number(data.get("gender")), guardianName: data.get("guardianName"), guardianPhone: phone, branchId, sportId, groupId: groupId || null, planId, paymentMethod: waived?"Online":method, cashSessionId: !waived&&method === "Cash" ? cashSessionId : null, discountRuleId: ruleId || null, expectedAmount: quote.finalAmount, exceptionalDiscount, trainingLevelId: group?.registrationFormat !== "Group" ? group?.trainingLevelId : null, levelReviewed: data.has("levelReviewed") }, { "Idempotency-Key": key.current }, { announce: false });
      const result = await response.json() as { playerId: string };
      const photo = data.get("photo");
      if (photo instanceof File && photo.size) {
        try { await uploadAsset(photo, "Photo", { playerId: result.playerId }); }
        catch { setPhotoIssue({ playerId: result.playerId, waived }); return; }
      }
      announceSaveSuccess(waived ? "تم تسجيل اللاعب واعتماد الإعفاء." : "تم تسجيل اللاعب وتأكيد التحصيل.");
      router.push("/dashboard/players");
    } catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر حفظ التسجيل."); }
    finally { setBusy(false); }
  }
  return <>{heading}<form className="form-shell" onSubmit={e => void submit(e)}>
    <h2>بيانات اللاعب</h2><label>اسم اللاعب بالعربية<input name="playerName" required /></label><label>تاريخ الميلاد<input name="birth" type="date" required /></label><label>النوع<select name="gender" required><option value="1">ذكر</option><option value="2">أنثى</option></select></label>
    <label>صورة اللاعب (اختياري)<FilePicker aria-label="صورة اللاعب (اختياري)" name="photo"  accept="image/jpeg,image/png,image/webp" /><small>صورة حتى 8 ميجابايت، تظهر للأشخاص المصرح لهم فقط.</small></label>
    <h2>ولي الأمر</h2><label>اسم ولي الأمر<input name="guardianName" required /></label><label>رقم الهاتف<input name="phone" dir="ltr" required value={phone} onChange={e => { setPhone(e.target.value); clearQuote(); }} /></label>
    <h2>المجموعة والباقة</h2><label>الفرع<select value={branchId} onChange={e => { setBranch(e.target.value); setGroup(""); setPlan(""); setCashSession(""); clearQuote(); }} required><option value="">اختر الفرع</option>{options.branches.map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select></label>
    <label>الرياضة<select value={sportId} onChange={e => { setSport(e.target.value); setGroup(""); setPlan(""); setCashSession(""); clearQuote(); }} required><option value="">اختر الرياضة</option>{options.sports.map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select></label>
    <label>الفئة العمرية<select value={categoryId} onChange={e => { setCategory(e.target.value); setGroup(""); clearQuote(); }} required={requiresGroup}><option value="">اختر الفئة</option>{options.categories.map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select></label>
    <label>المجموعة<select name="groupId" value={groupId} onChange={e => { setGroup(e.target.value); clearQuote(); }} required={requiresGroup}><option value="">{requiresGroup ? "اختر المجموعة" : "التسكين لاحقًا"}</option>{groups.map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select></label>
    {group && group.registrationFormat && group.registrationFormat !== "Group" && <label className="placement-choice"><input type="checkbox" name="levelReviewed" key={group.id} required /><span>راجعت مستوى اللاعب ويطابق مستوى المجموعة: {group.trainingLevel ?? "حدد مستوى المجموعة أولًا"}</span></label>}
    <label>الباقة<select aria-label="الباقة" value={planId} onChange={e => { const plan = availablePlans.find(p => p.id === e.target.value); setPlan(e.target.value); setCashSession(""); if (group && !registrationMatchesProduct(group.registrationFormat, plan?.trainingFormat)) setGroup(""); if (plan?.sportId) setSport(plan.sportId); clearQuote(); }} required><option value="">اختر الباقة</option>{availablePlans.map(p => <option key={p.id} value={p.id}>{p.arabicName} · {options.sports.find(s => s.id === p.sportId)?.arabicName} · {formatMoneyAr(p.price, p.currency)}</option>)}</select>{sportId && availablePlans.length === 0 && <small>لا توجد باقات نشطة لهذه الرياضة. أضف أو فعّل باقة من قسم الباقات.</small>}</label>
    <label>عرض دائم للاعب<select value={ruleId} onChange={e => { setRule(e.target.value); clearQuote(); }}><option value="">لا يوجد عرض إضافي</option>{rules.map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select><small>عرض الإخوة يُحتسب تلقائيًا عند وجود طفل آخر باشتراك مدفوع وفعال. لا تُجمع الخصومات.</small></label>
    {allowExceptional&&<fieldset disabled={busy}><legend>الخصم الاستثنائي</legend><label><input type="checkbox" checked={exceptional} onChange={e=>{setExceptional(e.target.checked);clearQuote();}}/>إضافة خصم استثنائي</label>{exceptional&&<><label>نوع الخصم<select value={exceptionType} onChange={e=>{setExceptionType(Number(e.target.value));clearQuote();}}><option value={2}>قيمة ثابتة</option><option value={1}>نسبة مئوية</option></select></label><label>قيمة الخصم<input type="number" required min="0.01" step="0.01" max={exceptionType===1?100:undefined} value={exceptionValue} onChange={e=>{setExceptionValue(e.target.value);clearQuote();}}/></label><label>سبب الخصم<textarea required maxLength={500} value={exceptionReason} onChange={e=>{setExceptionReason(e.target.value);clearQuote();}}/></label><label><input type="checkbox" checked={replaceDiscount} onChange={e=>{setReplaceDiscount(e.target.checked);clearQuote();}}/>استبدال أي خصم قائم بعد المراجعة</label></>}</fieldset>}
    <button type="button" disabled={!canQuote} onClick={() => void requestQuote()}>
      {quoteBusy ? "جارٍ حساب المبلغ…" : "حساب المبلغ المستحق"}
    </button>
    {quoteError && <ErrorState message={quoteError} />}
    {quote && <div className="adjustment-preview"><span>السعر الأصلي: {formatMoneyAr(quote.originalAmount, quote.currency)}</span><span>الخصم: {formatMoneyAr(quote.discountAmount, quote.currency)}{quote.discountReason && ` · ${quote.discountReason}`}</span><b>المبلغ المطلوب تحصيله: {formatMoneyAr(quote.finalAmount, quote.currency)}</b></div>}
    {quote && !waived && <>
      <label className="registration-payment-method">طريقة التحصيل<select name="paymentMethod" required value={method} disabled={busy} onChange={e => { setMethod(e.target.value); setCashSession(""); }}><option value="">اختر طريقة التحصيل</option><option value="Cash">نقدي من وردية مخولة</option><option value="Online">تحويل إلكتروني تم استلامه</option></select></label>
      {method === "Cash" && <RegistrationCashShift branchId={branchId} planId={planId} value={cashSessionId} onChange={setCashSession} disabled={busy} />}
      <p className="demo-note">أكّد التسجيل بعد استلام المبلغ. التأكيد يفعّل الاشتراك ويصدر الإيصال.</p>
    </>}
    {error && <ErrorState message={error} />}
    <div className="form-actions"><button type="submit" className="primary-button" disabled={!canRegister}>{busy ? "جارٍ التسجيل…" : waived ? "اعتماد الإعفاء والتسجيل" : "تسجيل وتأكيد التحصيل"}</button><button type="button" disabled={busy} onClick={() => router.back()}>إلغاء</button></div>
  </form></>;
}
