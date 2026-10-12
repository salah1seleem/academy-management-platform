"use client";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { FormEvent, useCallback, useEffect, useState } from "react";
import { ArrowRightLeft, Landmark, LockKeyhole, Wallet } from "lucide-react";
import { EmptyState, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { csrfRequest } from "./dashboard-api";
import { useStaffAction } from "./use-staff-action";
import { clearFinancialIntent, pendingFinancialIntent } from "../lib/pending-financial-intent";
import { formatDateTimeAr, formatMoneyAr } from "../lib/formatters";
import { CashCollectionForm } from "./cash-collection-form";

const root = "/api/v1/finance/cash-registers";
const route = "/dashboard/finance/cash-registers";
type Register = { id: string; name: string; branch: string; currency: string; canOpen: boolean; cashiers: { membershipId: string; isActive: boolean; name: string }[]; session?: { id: string; balance: string; opening: string; openedAtUtc: string; canClose: boolean } };
type Data = { cashEnabled: boolean; items: Register[]; history: { id: string; registerId: string; closedAtUtc: string; countedAmount: string; expectedAmount: string }[] };
type Options = { isOwner: boolean; branches: { id: string; name: string }[]; members: { id: string; name: string }[] };
async function read<T>(url: string): Promise<T> {
  const result = await fetch(url, { cache: "no-store" });
  if (!result.ok) throw new Error("تعذر تحميل الصندوق أو أنه خارج صلاحياتك.");
  return result.json();
}
function useCashWrite() {
  const [busy, setBusy] = useState(false), [error, setError] = useState("");
  async function run(path: string, body: unknown, done: () => Promise<void> | void, method: "POST" | "PUT" = "POST") {
    if (busy) return; setBusy(true); setError(""); const scope = `cash:${path}`;
    try {
      await csrfRequest(`${root}${path}`, method, body, { "Idempotency-Key": await pendingFinancialIntent(scope, body) });
      clearFinancialIntent(scope); await done();
    } catch (e) { setError(e instanceof Error ? e.message : "تعذر تسجيل عملية الصندوق."); }
    finally { setBusy(false); }
  }
  return { run, busy, error };
}
export function CashRegisters() {
  const [data, setData] = useState<Data | null>(null), [options, setOptions] = useState<Options | null>(null), [error, setError] = useState("");
  const write = useCashWrite(), canAdd = useStaffAction("finance", "add");
  const load = useCallback(async () => {
    const [d, o] = await Promise.all([read<Data>(root), read<Options>(`${root}/options`)]); setData(d); setOptions(o);
  }, []);
  useEffect(() => { let active = true; void Promise.all([read<Data>(root), read<Options>(`${root}/options`)]).then(([d, o]) => { if (active) { setData(d); setOptions(o); } }).catch(e => { if (active) setError(e.message); }); return () => { active = false; }; }, []);
  function create(e: FormEvent<HTMLFormElement>) {
    e.preventDefault(); const form = e.currentTarget, f = new FormData(form);
    void write.run("", { branchId: f.get("branch"), name: f.get("name") }, async () => { form.reset(); await load(); });
  }
  function transfer(e: FormEvent<HTMLFormElement>) {
    e.preventDefault(); const form = e.currentTarget, f = new FormData(form);
    if (!window.confirm("تأكيد نقل العهدة بين الصندوقين؟ التحويل ليس إيرادًا جديدًا.")) return;
    void write.run("/transfers", { fromSessionId: f.get("from"), toSessionId: f.get("to"), amount: f.get("amount"), reason: f.get("reason") || null }, async () => { form.reset(); await load(); });
  }
  const open = data?.items.filter(r => r.session) ?? [];
  return <><PageHeader title="الصناديق والعهد" context="المالية" description="كل فرع بصندوق وأمين مخول. العهدة الافتتاحية والتحويل بين الصناديق ليسا تحصيلًا أو إيرادًا." />
    {(error || write.error) && <ErrorState message={error || write.error} />}
    {!data || !options ? !error && <LoadingState /> : <>
      <div className="cash-intro"><Wallet aria-hidden="true" /><div><strong>{data.cashEnabled ? "النقدي مفعل في إعدادات الأكاديمية" : "النقدي متوقف"}</strong><p>{data.cashEnabled ? "اقبض اشتراكًا من تفاصيل ورديتك المفتوحة، ثم راجع الإيصال ورصيد الصندوق." : "يمكن تجهيز الصناديق الآن. لا يفتح أمين وردية قبل تفعيل النقدي من المالك."}</p></div>{options.isOwner && <Link href="/dashboard/settings">إعدادات الأكاديمية</Link>}</div>
      <div className="cash-register-grid">{data.items.map(r => <article className="cash-register-card" key={r.id} aria-label={`صندوق ${r.name}`}>
        <header><span className="cash-symbol"><Landmark aria-hidden="true" /></span><div><h2>{r.name}</h2><p>{r.branch}</p></div><span className="status-pill">{r.session ? "وردية مفتوحة" : "مقفل"}</span></header>
        {r.session ? <><span className="cash-balance-label">الرصيد الدفتري للوردية</span><strong className="cash-balance">{formatMoneyAr(r.session.balance, r.currency)}</strong><p>عهدة افتتاحية: {formatMoneyAr(r.session.opening, r.currency)} · {formatDateTimeAr(r.session.openedAtUtc)}</p><Link className="primary-button" href={`${route}/sessions/${r.session.id}`}>تفاصيل الوردية والمطابقة</Link></> : <p className="muted">لا توجد وردية مفتوحة لهذا الصندوق.</p>}
        <p className="cash-cashiers"><LockKeyhole size={16} aria-hidden="true" /> الأمناء: {r.cashiers.filter(c => c.isActive).map(c => c.name).join("، ") || "لم يعيّن المالك أمينًا بعد"}</p>
        {!r.session && r.canOpen && canAdd && data.cashEnabled && <details><summary>فتح وردية</summary><form onSubmit={e => { e.preventDefault(); const form = e.currentTarget, f = new FormData(form); void write.run(`/${r.id}/open`, { openingAmount: f.get("opening") }, async () => { form.reset(); await load(); }); }}><label>العهدة الافتتاحية<input name="opening" type="number" min="0" max="999999999" step="0.01" defaultValue="0.00" required /></label><small>المبلغ الموجود فعليًا في الصندوق؛ ليس إيرادًا.</small><button className="primary-button" disabled={write.busy}>تأكيد فتح الوردية</button></form></details>}
        {options.isOwner && <details><summary>صلاحيات أمناء الصندوق</summary><form onSubmit={e => { e.preventDefault(); const f = new FormData(e.currentTarget); void write.run(`/${r.id}/cashiers`, { membershipId: f.get("member"), enabled: f.get("enabled") === "on" }, load, "PUT"); }}><label>أمين الصندوق<select name="member" required><option value="">اختر الموظف</option>{options.members.map(m => <option key={m.id} value={m.id}>{m.name}</option>)}</select></label><label className="checkbox-line"><input type="checkbox" name="enabled" defaultChecked /> السماح بإدارة ورديته</label><small>لا يمنح هذا فرعًا أو صلاحية مالية عامة؛ يلزم منحها مستقلًا.</small><button className="secondary-button" disabled={write.busy}>حفظ تفويض الأمين</button></form></details>}
      </article>)}</div>
      {!data.items.length && <EmptyState title="جهّز أول صندوق" message="يضيف المالك صندوق الفرع ثم يفوض الأمين ويفتح الوردية. لا ينشأ قبض تلقائيًا." />}
      {options.isOwner && <section className="detail-card"><details><summary>إضافة صندوق فرع</summary><form className="form-shell" onSubmit={create}><label>اسم الصندوق<input name="name" required maxLength={100} placeholder="مثلًا: صندوق الفرع الرئيسي" /></label><label>الفرع<select name="branch" required><option value="">اختر الفرع</option>{options.branches.map(b => <option key={b.id} value={b.id}>{b.name}</option>)}</select></label><div className="form-actions"><button className="primary-button" disabled={write.busy}>إنشاء الصندوق</button></div></form></details></section>}
      {options.isOwner && open.length >= 2 && data.cashEnabled && <section className="detail-card"><details><summary><ArrowRightLeft size={18} aria-hidden="true" /> تحويل عهدة بين صندوقين</summary><form className="form-shell" onSubmit={transfer}><label>الصندوق المصدر<select name="from" required><option value="">اختر المصدر</option>{open.map(r => <option key={r.id} value={r.session!.id}>{r.name} · {formatMoneyAr(r.session!.balance, r.currency)}</option>)}</select></label><label>الصندوق المستلم<select name="to" required><option value="">اختر المستلم</option>{open.map(r => <option key={r.id} value={r.session!.id}>{r.name}</option>)}</select></label><label>المبلغ المحول<input name="amount" type="number" min="0.01" max="999999999" step="0.01" required /></label><label>السبب (اختياري)<input name="reason" maxLength={400} /></label><div className="form-actions"><button className="primary-button" disabled={write.busy}>تأكيد التحويل</button></div></form></details></section>}
      {!!data.history.length && <section className="detail-card"><h2>آخر الورديات المقفلة</h2><div className="linked-list">{data.history.map(s => <article key={s.id}><div><strong>{data.items.find(r => r.id === s.registerId)?.name}</strong><small>{formatDateTimeAr(s.closedAtUtc)}</small></div><span>المتوقع {formatMoneyAr(s.expectedAmount, data.items.find(r => r.id === s.registerId)?.currency)} · الفعلي {formatMoneyAr(s.countedAmount, data.items.find(r => r.id === s.registerId)?.currency)}</span><Link href={`${route}/sessions/${s.id}`}>عرض المطابقة</Link></article>)}</div></section>}
    </>}
  </>;
}
type SessionData = { name: string; currency: string; balance: string; variance: string | null; canClose: boolean; canCollect?: boolean; refunds?: { id: string; refundId: string; playerId: string; amount: string; currency: string; reason?: string; createdAtUtc: string }[]; productSales?: { id: string; productSaleId: string; amount: string; currency: string; productName: string; quantity: number; createdAtUtc: string }[]; receipts?: { id: string; receiptId: string; amount: string; currency: string; receiptNumber: string; playerNameSnapshot: string; planNameSnapshot: string; createdAtUtc: string }[]; session: { openingAmount: string; openedAtUtc: string; closedAtUtc?: string; expectedAmount?: string; countedAmount?: string; closingReason?: string }; movements: { id: string; kind: string; amount: string; createdAtUtc: string }[] };
export function CashSessionDetail({ id }: { id: string }) {
  const [data, setData] = useState<SessionData | null>(null), [error, setError] = useState("");
  const router = useRouter(), write = useCashWrite(), canAdd = useStaffAction("finance", "add");
  useEffect(() => { let active = true; void read<SessionData>(`${root}/sessions/${id}`).then(d => { if (active) setData(d); }).catch(e => { if (active) setError(e.message); }); return () => { active = false; }; }, [id]);
  function close(e: FormEvent<HTMLFormElement>) {
    e.preventDefault(); const f = new FormData(e.currentTarget);
    if (!window.confirm("إقفال الوردية وتثبيت المطابقة؟ لا يمكن تعديلها بعد الإقفال.")) return;
    void write.run(`/sessions/${id}/close`, { countedAmount: f.get("counted"), reason: f.get("reason") || null }, () => router.push(route));
  }
  return <><PageHeader title="مطابقة وردية الصندوق" context="المالية / الصناديق" action={{ label: "العودة للصناديق", href: route, tone: "secondary" }} />{(error || write.error) && <ErrorState message={error || write.error} />}{!data ? !error && <LoadingState /> : <>
    <section className="detail-card"><h2>{data.name}</h2><p>{data.session.closedAtUtc ? `وردية مقفلة ${formatDateTimeAr(data.session.closedAtUtc)}` : `مفتوحة منذ ${formatDateTimeAr(data.session.openedAtUtc)}`}</p><div className="cash-session-stats"><div><small>العهدة الافتتاحية</small><strong>{formatMoneyAr(data.session.openingAmount, data.currency)}</strong></div><div><small>الرصيد الدفتري</small><strong>{formatMoneyAr(data.balance, data.currency)}</strong></div>{data.session.closedAtUtc && <><div><small>المبلغ الفعلي عند الإقفال</small><strong>{formatMoneyAr(data.session.countedAmount, data.currency)}</strong></div><div><small>فرق المطابقة</small><strong>{formatMoneyAr(data.variance, data.currency)}</strong></div></>}</div>{data.session.closingReason && <p>{data.session.closingReason}</p>}</section>
    {!data.session.closedAtUtc && data.canCollect && canAdd && <CashCollectionForm key={id} sessionId={id} onCollected={async () => setData(await read<SessionData>(`${root}/sessions/${id}`))} />}
    {!!data.receipts?.length && <section className="detail-card"><h2>إيصالات القبض في الوردية</h2><div className="linked-list">{data.receipts.map(r => <article key={r.id}><div><strong>{r.playerNameSnapshot}</strong><small>{r.planNameSnapshot} · {r.receiptNumber}</small></div><span>{formatMoneyAr(r.amount, r.currency)}</span><Link href={`/dashboard/subscriptions/receipts/${r.receiptId}`}>عرض الإيصال</Link></article>)}</div></section>}
    {!!data.productSales?.length && <section className="detail-card"><h2>مبيعات المنتجات النقدية</h2><div className="linked-list">{data.productSales.map(p => <article key={p.id}><div><strong>{p.productName}</strong><small>الكمية {p.quantity} · {formatDateTimeAr(p.createdAtUtc)}</small></div><span>{formatMoneyAr(p.amount, p.currency)}</span><Link href={`/dashboard/product-sales/${p.productSaleId}`}>تفاصيل البيع</Link></article>)}</div></section>}
    {!data.session.closedAtUtc && data.canClose && canAdd && <section className="detail-card"><h2>عدّ النقدية ثم طابق</h2><p>الفارق يُسجل للمراجعة ولا يتحول إلى تحصيل أو مصروف تلقائيًا.</p><form className="form-shell" onSubmit={close}><label>المبلغ الموجود فعليًا<input name="counted" type="number" min="0" max="999999999" step="0.01" required /></label><label>ملاحظة المطابقة (اختياري)<input name="reason" maxLength={400} /></label><div className="form-actions"><button className="primary-button" disabled={write.busy}>تثبيت المطابقة وإقفال الوردية</button></div></form></section>}
    {!!data.refunds?.length && <section className="detail-card"><h2>الاستردادات النقدية المصروفة</h2><div className="linked-list">{data.refunds.map(r => <article key={r.id}><div><strong>{r.reason || "استرداد جزئي"}</strong><small>{formatDateTimeAr(r.createdAtUtc)}</small></div><span>صرف {formatMoneyAr(r.amount, r.currency)}</span><Link href={`/dashboard/players/${r.playerId}?tab=subscriptions`}>ملف اللاعب والاسترداد</Link></article>)}</div><p className="muted">تُخصم من رصيد الوردية دون حذف إيصال التحصيل أو إعادة فتح دين.</p></section>}
    <section className="detail-card"><h2>حركات العهدة</h2><div className="linked-list">{data.movements.map(m => <article key={m.id}><strong>{m.kind === "TransferIn" ? "عهدة مستلمة" : "عهدة محولة"}</strong><span>{formatMoneyAr(m.amount, data.currency)}</span><small>{formatDateTimeAr(m.createdAtUtc)}</small></article>)}</div>{!data.movements.length && <p className="muted">لا توجد حركات للوردية. العهدة الافتتاحية محفوظة بشكل مستقل.</p>}</section>
  </>}</>;
}
