"use client";
import Link from "next/link";
import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { ArrowRightLeft, Landmark, LockKeyhole, Wallet } from "lucide-react";
import { ConfirmationDialog, EmptyState, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { announceSaveSuccess, csrfRequest, DashboardRequestError } from "./dashboard-api";
import { useStaffAction } from "./use-staff-action";
import { clearFinancialIntent, pendingFinancialIntent } from "../lib/pending-financial-intent";
import { formatDateTimeAr, formatMoneyAr } from "../lib/formatters";
import { canonicalDigits } from "../lib/number-input";
import { CashCollectionForm } from "./cash-collection-form";

const root = "/api/v1/finance/cash-registers";
const route = "/dashboard/finance/cash-registers";
type Register = { id: string; name: string; branch: string; currency: string; canOpen: boolean; cashiers: { membershipId: string; isActive: boolean; name: string }[]; session?: { id: string; balance: string; opening: string; openedAtUtc: string; canClose: boolean } };
type Data = { total?: number; offset?: number; pageSize?: number; cashEnabled: boolean; items: Register[]; history: { id: string; registerId: string; closedAtUtc: string; countedAmount: string; expectedAmount: string }[] };
type Options = { isOwner: boolean; branches: { id: string; name: string }[]; members: { id: string; name: string }[] };
async function read<T>(url: string, signal?: AbortSignal): Promise<T> {
  const result = await fetch(url, { cache: "no-store", signal });
  if (!result.ok) throw new DashboardRequestError("تعذر تحميل الصندوق أو أنه خارج صلاحياتك.", result.status);
  return result.json();
}
function useCashWrite() {
  const [busy, setBusy] = useState(false), [error, setError] = useState("");
  const running = useRef(false);
  async function run(path: string, body: unknown, done: () => Promise<void> | void, method: "POST" | "PUT" = "POST") {
    if (running.current) return;
    running.current = true;
    setBusy(true); setError(""); const scope = `cash:${path}`;
    try {
      await csrfRequest(`${root}${path}`, method, body, { "Idempotency-Key": await pendingFinancialIntent(scope, body) });
      clearFinancialIntent(scope); await done();
    } catch (e) { setError(e instanceof Error ? e.message : "تعذر تسجيل عملية الصندوق."); }
    finally { running.current = false; setBusy(false); }
  }
  return { run, busy, error };
}
export function CashRegisters() {
  const [search, setSearch] = useState(""), [offset, setOffset] = useState(0);
  const filtered = Boolean(search.trim() || offset);
  const listUrl = filtered ? `${root}?${new URLSearchParams({ search: search.trim(), offset: String(offset) })}` : root;
  const [revision, setRevision] = useState(0);
  const [result, setResult] = useState<{ url: string; revision: number; data: Data; options: Options } | { url: string; revision: number; error: unknown }>();
  const current = result?.url === listUrl ? result : undefined;
  const data = current && "data" in current ? current.data : undefined;
  const options = current && "options" in current ? current.options : undefined;
  const failure = current && "error" in current && current.revision === revision ? current.error : undefined;
  const refreshing = current?.revision !== revision;
  const write = useCashWrite(), canAdd = useStaffAction("finance", "add");
  const reload = useCallback(() => setRevision(value => value + 1), []);
  const clearFilters = () => { setSearch(""); setOffset(0); setResult(undefined); };
  useEffect(() => {
    const controller = new AbortController();
    const signal = controller.signal;
    void Promise.all([read<Data>(listUrl, signal), read<Options>(`${root}/options`, signal)])
      .then(([data, options]) => { if (!signal.aborted) setResult({ url: listUrl, revision, data, options }); })
      .catch(error => { if (!signal.aborted) setResult({ url: listUrl, revision, error }); });
    return () => controller.abort();
  }, [listUrl, revision]);
  function create(e: FormEvent<HTMLFormElement>) {
    e.preventDefault(); const form = e.currentTarget, f = new FormData(form);
    void write.run("", { branchId: f.get("branch"), name: f.get("name") }, () => { form.reset(); clearFilters(); reload(); });
  }
  return <><PageHeader title="الصناديق والعهد" context="المالية" />
    <div className="filter-toolbar"><label>البحث باسم الصندوق<input value={search} maxLength={100} onChange={event => {
      if (event.target.value.trim() !== search.trim() || offset) setResult(undefined);
      setSearch(event.target.value);
      setOffset(0);
    }} /></label>{(search || offset > 0) && <button className="secondary-button" onClick={() => { clearFilters(); reload(); }}>مسح الفلاتر</button>}</div>
    {write.error && <ErrorState message={write.error} />}
    {failure ? <><ErrorState message={inaccessible(failure) ? "الصناديق غير متاحة لهذا الحساب." : "تعذر تحميل الصناديق."} />{!inaccessible(failure) && <button className="secondary-button" type="button" onClick={reload}>إعادة المحاولة</button>}</>
      : !data || !options ? <LoadingState /> : <>
      <div className="cash-intro"><Wallet aria-hidden="true" /><div><strong>{data.cashEnabled ? "النقدي مفعل" : "النقدي متوقف"}</strong>{!data.cashEnabled && <p>يلزم تفعيل النقدي لفتح وردية.</p>}</div>{options.isOwner && <Link href="/dashboard/settings?tab=advanced">إعدادات الأكاديمية</Link>}</div>
      <div className="cash-register-grid">{data.items.map(r => <article className="cash-register-card" key={r.id} aria-label={`صندوق ${r.name}`}>
        <header><span className="cash-symbol"><Landmark aria-hidden="true" /></span><div><h2>{r.name}</h2><p>{r.branch}</p></div><span className="status-pill">{r.session ? "وردية مفتوحة" : "مقفل"}</span></header>
        {r.session ? <><span className="cash-balance-label">الرصيد الدفتري للوردية</span><strong className="cash-balance">{formatMoneyAr(r.session.balance, r.currency)}</strong><p>عهدة افتتاحية: {formatMoneyAr(r.session.opening, r.currency)} · {formatDateTimeAr(r.session.openedAtUtc)}</p><Link className="primary-button" href={`${route}/sessions/${r.session.id}`}>تفاصيل الوردية والمطابقة</Link></> : <p className="muted">لا توجد وردية مفتوحة لهذا الصندوق.</p>}
        <p className="cash-cashiers"><LockKeyhole size={16} aria-hidden="true" /> الأمناء: {r.cashiers.filter(c => c.isActive).map(c => c.name).join("، ") || "لم يعيّن المالك أمينًا بعد"}</p>
        {!r.session && r.canOpen && canAdd && data.cashEnabled && <details><summary>فتح وردية</summary><form onSubmit={e => { e.preventDefault(); const form = e.currentTarget, f = new FormData(form); void write.run(`/${r.id}/open`, { openingAmount: f.get("opening") }, () => { form.reset(); reload(); }); }}><label>العهدة الافتتاحية<input name="opening" type="number" min="0" max="999999999" step="0.01" defaultValue="0.00" required /></label><small>المبلغ الموجود فعليًا في الصندوق؛ ليس إيرادًا.</small><button className="primary-button" disabled={write.busy}>تأكيد فتح الوردية</button></form></details>}
        {options.isOwner && <details><summary>صلاحيات أمناء الصندوق</summary><form onSubmit={e => { e.preventDefault(); const f = new FormData(e.currentTarget); void write.run(`/${r.id}/cashiers`, { membershipId: f.get("member"), enabled: f.get("enabled") === "on" }, reload, "PUT"); }}><label>أمين الصندوق<select aria-label="أمين الصندوق" name="member" required><option value="">اختر الموظف</option>{options.members.map(m => <option key={m.id} value={m.id}>{m.name}</option>)}</select></label><label className="checkbox-line"><input type="checkbox" name="enabled" defaultChecked /> السماح بإدارة ورديته</label><small>لا يمنح هذا فرعًا أو صلاحية مالية عامة؛ يلزم منحها مستقلًا.</small><button className="secondary-button" disabled={write.busy}>حفظ تفويض الأمين</button></form></details>}
      </article>)}</div>
      {!data.items.length && <EmptyState title={filtered ? "لا توجد صناديق مطابقة" : options.isOwner ? "جهّز أول صندوق" : "لا توجد صناديق متاحة"} message={filtered ? "غيّر البحث أو امسح الفلاتر." : options.isOwner ? "أضف صندوق الفرع وحدّد أمينه لفتح أول وردية." : "تظهر هنا صناديق الفروع المصرّح لك بها."} />}
      {(data.total ?? 0) > (data.pageSize ?? 100) && <nav className="list-pagination" aria-label="صفحات الصناديق"><button disabled={offset === 0} onClick={() => setOffset(v => Math.max(0, v - (data.pageSize ?? 100)))}>السابق</button><span>صفحة {Math.floor(offset / (data.pageSize ?? 100)) + 1} من {Math.ceil(data.total! / (data.pageSize ?? 100))}</span><button disabled={offset + (data.pageSize ?? 100) >= data.total!} onClick={() => setOffset(v => v + (data.pageSize ?? 100))}>التالي</button></nav>}
      {options.isOwner && <section className="detail-card"><details><summary>إضافة صندوق فرع</summary><form className="form-shell" onSubmit={create}><label>اسم الصندوق<input name="name" required maxLength={100} placeholder="مثلًا: صندوق الفرع الرئيسي" /></label><label>الفرع<select aria-label="الفرع" name="branch" required><option value="">اختر الفرع</option>{options.branches.map(b => <option key={b.id} value={b.id}>{b.name}</option>)}</select></label><div className="form-actions"><button className="primary-button" disabled={write.busy}>إنشاء الصندوق</button></div></form></details></section>}
      {options.isOwner && data.cashEnabled && <CashTransfer key={listUrl} registers={data.items} refreshing={refreshing} onChanged={reload} />}
      {!!data.history.length && <section className="detail-card cash-history"><h2>آخر الورديات المقفلة</h2><div className="linked-list">{data.history.map(shift => {
        const register = data.items.find(r => r.id === shift.registerId);
        const difference = hasCashAmount(shift.countedAmount) && hasCashAmount(shift.expectedAmount) ? cashDifference(shift.countedAmount, shift.expectedAmount) : null;
        return <article key={shift.id}><div><strong>{register?.name ?? "صندوق الوردية"}</strong><small>{formatDateTimeAr(shift.closedAtUtc)}</small></div><div><small>المتوقع {displayCash(shift.expectedAmount, register?.currency)}</small><small>الفعلي {displayCash(shift.countedAmount, register?.currency)}</small><strong>{difference === null ? "فرق المطابقة غير متاح" : varianceLabel(difference, register?.currency)}</strong></div><Link href={`${route}/sessions/${shift.id}`}>عرض المطابقة</Link></article>;
      })}</div></section>}
    </>}
  </>;
}

type TransferRequest = {
  fromSessionId: string;
  toSessionId: string;
  amount: string;
  reason: string | null;
  expectedSourceBalance: string;
  expectedTargetBalance: string;
};
type TransferReview = { request: TransferRequest; message: string };

function CashTransfer({ registers, refreshing, onChanged }: { registers: Register[]; refreshing: boolean; onChanged: () => void }) {
  const open = registers.flatMap(register => register.session ? [{ ...register, session: register.session }] : []);
  const [review, setReview] = useState<TransferReview | null>(null);
  const [pending, setPending] = useState<TransferRequest | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const form = useRef<HTMLFormElement>(null);
  const running = useRef(false);
  const scope = "cash:/transfers";

  function prepare(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (review || pending || running.current || refreshing) return;
    const fields = new FormData(event.currentTarget);
    const source = open.find(register => register.session.id === from);
    const target = open.find(register => register.session.id === to);
    if (!source || !target || from === to) { setError("اختر صندوقين مختلفين لهما ورديتان مفتوحتان."); return; }
    if (source.currency !== target.currency) { setError("اختر صندوقين لهما نفس العملة."); return; }
    if (!hasCashAmount(source.session.balance) || !hasCashAmount(target.session.balance)) {
      setError("تعذر التحقق من الرصيد الدفتري للصندوقين.");
      onChanged();
      return;
    }
    const raw = canonicalDigits(String(fields.get("amount") ?? "").trim()).replace("٫", ".");
    const amount = Number(raw);
    if (!/^\d{1,9}(?:\.\d{1,2})?$/.test(raw) || amount <= 0) {
      setError("أدخل مبلغًا أكبر من صفر بخانتين عشريتين على الأكثر.");
      return;
    }
    if (amount > Number(source.session.balance)) { setError("رصيد الصندوق المصدر لا يكفي."); return; }
    const money = (value: number) => formatMoneyAr(value, source.currency);
    const sourceAfter = cashDifference(source.session.balance, amount);
    const targetAfter = cashDifference(target.session.balance, -amount);
    setError("");
    setReview({
      request: {
        fromSessionId: from,
        toSessionId: to,
        amount: amount.toFixed(2),
        reason: String(fields.get("reason") ?? "").trim() || null,
        expectedSourceBalance: source.session.balance,
        expectedTargetBalance: target.session.balance,
      },
      message: `تحويل ${money(amount)} من ${source.name} إلى ${target.name}. الرصيد المقدّر بعد التحويل ${money(sourceAfter)} في المصدر و${money(targetAfter)} في المستلم.`,
    });
  }

  async function submit(request: TransferRequest) {
    if (running.current) return;
    running.current = true;
    setBusy(true);
    setError("");
    try {
      await csrfRequest(`${root}/transfers`, "POST", request,
        { "Idempotency-Key": await pendingFinancialIntent(scope, request) }, { announce: false });
      clearFinancialIntent(scope);
      setPending(null);
      setFrom("");
      setTo("");
      form.current?.reset();
      announceSaveSuccess("تم تحويل العهدة.");
      onChanged();
    } catch (caught) {
      const rejected = caught instanceof DashboardRequestError && caught.status >= 400 && caught.status < 500 && ![408, 429, 499].includes(caught.status);
      if (rejected) {
        clearFinancialIntent(scope);
        setPending(null);
        setError(caught.message);
        if ([403, 404, 409].includes(caught.status)) onChanged();
      } else {
        setPending(request);
        setError("تعذر تأكيد التحويل. أعد إرسال نفس التحويل للتحقق من نتيجته.");
      }
    } finally {
      setReview(null);
      running.current = false;
      setBusy(false);
    }
  }

  if (open.length < 2 && !pending && !error) return null;
  return <section className="detail-card"><details><summary><ArrowRightLeft size={18} aria-hidden="true" /> تحويل عهدة بين صندوقين</summary>
    <form ref={form} className="form-shell" noValidate onSubmit={prepare}>
      {error && <ErrorState message={error} />}
      {refreshing && <p role="status">جارٍ تحديث أرصدة الصناديق…</p>}
      <fieldset disabled={busy || pending !== null}>
        <label>الصندوق المصدر<select aria-label="الصندوق المصدر" required value={from} onChange={event => setFrom(event.target.value)}><option value="">اختر المصدر</option>{open.map(register => <option key={register.id} value={register.session.id}>{register.name} · {formatMoneyAr(register.session.balance, register.currency)}</option>)}</select></label>
        <label>الصندوق المستلم<select aria-label="الصندوق المستلم" required value={to} onChange={event => setTo(event.target.value)}><option value="">اختر المستلم</option>{open.map(register => <option key={register.id} value={register.session.id}>{register.name} · {formatMoneyAr(register.session.balance, register.currency)}</option>)}</select></label>
        <label>المبلغ المحول<input name="amount" type="text" inputMode="decimal" dir="ltr" maxLength={12} required /></label>
        <label>السبب (اختياري)<input name="reason" maxLength={400} /></label>
      </fieldset>
      <div className="form-actions">
        {pending ? <button className="primary-button" type="button" disabled={busy} onClick={() => void submit(pending)}>إعادة محاولة التحويل</button>
          : <button className="primary-button" disabled={busy || refreshing || open.length < 2}>مراجعة التحويل</button>}
      </div>
    </form>
  </details>
    <ConfirmationDialog open={review !== null} title="راجع تحويل العهدة" message={review?.message ?? ""} confirmLabel="تأكيد التحويل" tone="primary" busy={busy}
      onCancel={() => setReview(null)} onConfirm={() => { if (review) void submit(review.request); }} />
  </section>;
}

type SessionData = { externalReceipts?:{id:string;receiptId:string;amount:number;currency:string;receiptNumber:string;agreementId:string;title:string;counterpartyName:string;createdAtUtc:string}[]; name: string; currency: string; balance: string; variance: string | null; canClose: boolean; canCollect?: boolean; canCrossBranchPayment?: boolean; refunds?: { id: string; refundId: string; receiptId?: string; canReadPlayer?: boolean; voidReceiptId?: string; playerId: string; amount: string; currency: string; reason?: string; createdAtUtc: string }[]; productSales?: { id: string; productSaleId: string; amount: string; currency: string; productName: string; quantity: number; createdAtUtc: string }[]; receipts?: { id: string; receiptId: string; amount: string; currency: string; receiptNumber: string; playerNameSnapshot: string; planNameSnapshot: string; createdAtUtc: string }[]; session: { id: string; openingAmount: string; openedAtUtc: string; closedAtUtc?: string; expectedAmount?: string; countedAmount?: string; closingReason?: string }; movements: { id: string; kind: string; amount: string; createdAtUtc: string }[] };
const sessionPath = (id: string) => `${root}/sessions/${encodeURIComponent(id)}`;
const inaccessible = (error: unknown) => error instanceof DashboardRequestError && [401, 403, 404].includes(error.status);
const hasCashAmount = (value: unknown) => typeof value === "number" ? Number.isFinite(value)
  : typeof value === "string" && /^-?\d+(?:\.\d{1,2})?$/.test(value);
async function readSession(id: string) {
  const data = await read<SessionData>(sessionPath(id));
  if (data?.session?.id !== id || !data.name || !data.currency || !Array.isArray(data.movements) ||
      !hasCashAmount(data.balance) || !hasCashAmount(data.session.openingAmount) ||
      (data.session.closedAtUtc && ![data.session.expectedAmount, data.session.countedAmount, data.variance].every(hasCashAmount))) {
    throw new Error("تعذر التحقق من الوردية.");
  }
  return data;
}
function cashDifference(counted: number | string, expected: number | string) {
  return (Math.round(Number(counted) * 100) - Math.round(Number(expected) * 100)) / 100;
}
function varianceLabel(value: number, currency?: string) {
  if (value === 0) return "متطابق";
  return `${value < 0 ? "عجز" : "زيادة"} ${formatMoneyAr(Math.abs(value), currency)}`;
}

export function CashSessionDetail({ id }: { id: string }) {
  return <CashSessionScreen key={id} id={id} />;
}

function CashSessionScreen({ id }: { id: string }) {
  const [data, setData] = useState<SessionData | null>(null);
  const [failure, setFailure] = useState<"denied" | "retry" | null>(null);
  const [revision, setRevision] = useState(0);
  const summary = useRef<HTMLElement>(null);
  const focusSaved = useRef(false);
  const canAdd = useStaffAction("finance", "add");
  useEffect(() => {
    let active = true;
    void readSession(id).then(d => { if (active) setData(d); })
      .catch(error => { if (active) setFailure(inaccessible(error) ? "denied" : "retry"); });
    return () => { active = false; };
  }, [id, revision]);
  useEffect(() => {
    if (data?.session.closedAtUtc && focusSaved.current) {
      focusSaved.current = false;
      summary.current?.focus();
    }
  }, [data?.session.closedAtUtc]);
  return <><PageHeader title="مطابقة وردية الصندوق" context="المالية / الصناديق" action={{ label: "العودة للصناديق", href: route, tone: "secondary" }} />
    {failure === "denied" ? <ErrorState message="تعذر تحميل الصندوق أو أنه خارج صلاحياتك." />
      : failure === "retry" ? <><ErrorState message="تعذر تحميل تفاصيل الوردية." /><button className="secondary-button" type="button" onClick={() => { setFailure(null); setRevision(value => value + 1); }}>إعادة المحاولة</button></>
      : !data ? <LoadingState /> : <>
    <section className="detail-card" ref={summary} tabIndex={-1} aria-labelledby="cash-session-title"><h2 id="cash-session-title">{data.name}</h2><p role={data.session.closedAtUtc ? "status" : undefined}>{data.session.closedAtUtc ? `وردية مقفلة ${formatDateTimeAr(data.session.closedAtUtc)}` : `مفتوحة منذ ${formatDateTimeAr(data.session.openedAtUtc)}`}</p><div className="cash-session-stats"><div><small>العهدة الافتتاحية</small><strong>{formatMoneyAr(data.session.openingAmount, data.currency)}</strong></div><div><small>{data.session.closedAtUtc ? "الرصيد المتوقع عند الإقفال" : "الرصيد الدفتري"}</small><strong>{data.session.closedAtUtc ? displayCash(data.session.expectedAmount, data.currency) : formatMoneyAr(data.balance, data.currency)}</strong></div>{data.session.closedAtUtc && <><div><small>المبلغ الفعلي عند الإقفال</small><strong>{displayCash(data.session.countedAmount, data.currency)}</strong></div><div><small>فرق المطابقة</small><strong>{data.variance == null ? "—" : varianceLabel(Number(data.variance), data.currency)}</strong></div></>}</div>{data.session.closingReason && <p>{data.session.closingReason}</p>}</section>
    {!data.session.closedAtUtc && data.canCollect && canAdd && <><Link className="secondary-button" href={`${route}/sessions/${id}/family`}>تحصيل وتجديد عدة أبناء معًا</Link><CashCollectionForm key={id} sessionId={id} allowCrossBranchSearch={data.canCrossBranchPayment} onCollected={async () => setData(await readSession(id))} /></>}
    {!!data.receipts?.length && <section className="detail-card"><h2>إيصالات القبض في الوردية</h2><div className="linked-list">{data.receipts.map(r => <article key={r.id}><div><strong>{r.playerNameSnapshot}</strong><small>{r.planNameSnapshot} · {r.receiptNumber}</small></div><span>{formatMoneyAr(r.amount, r.currency)}</span><Link href={`/dashboard/subscriptions/receipts/${r.receiptId}`}>عرض الإيصال</Link></article>)}</div></section>}
    {!!data.productSales?.length && <section className="detail-card"><h2>مبيعات المنتجات النقدية</h2><div className="linked-list">{data.productSales.map(p => <article key={p.id}><div><strong>{p.productName}</strong><small>الكمية {p.quantity} · {formatDateTimeAr(p.createdAtUtc)}</small></div><span>{formatMoneyAr(p.amount, p.currency)}</span><Link href={`/dashboard/product-sales/${p.productSaleId}`}>تفاصيل البيع</Link></article>)}</div></section>}
    {!!data.externalReceipts?.length&&<section className="detail-card"><h2>تحصيل الاتفاقيات النقدي</h2><div className="linked-list">{data.externalReceipts.map(r=><article key={r.id}><div><strong>{r.counterpartyName}</strong><small>{r.title} · {r.receiptNumber}</small></div><span>{formatMoneyAr(r.amount,r.currency)}</span><Link href={`/dashboard/finance/external-agreements/${r.agreementId}/receipts/${r.receiptId}`}>الإيصال</Link></article>)}</div></section>}
    {!data.session.closedAtUtc && data.canClose && canAdd && <CashReconciliation data={data} onRead={next => { focusSaved.current = Boolean(next.session.closedAtUtc); setData(next); }} onDenied={() => { setData(null); setFailure("denied"); }} />}
    {!!data.refunds?.length && <section className="detail-card"><h2>الاستردادات النقدية المصروفة</h2><div className="linked-list">{data.refunds.map(r => <article key={r.id}><div><strong>{r.voidReceiptId ? "عكس إيصال نقدي" : r.reason || "استرداد جزئي"}</strong><small>{formatDateTimeAr(r.createdAtUtc)}</small></div><span>صرف {formatMoneyAr(r.amount, r.currency)}</span><Link href={r.voidReceiptId ? `/dashboard/subscriptions/receipts/${r.voidReceiptId}` : r.canReadPlayer === false ? `/dashboard/subscriptions/receipts/${r.receiptId}` : `/dashboard/players/${r.playerId}?tab=subscriptions`}>{r.voidReceiptId ? "الإيصال المعكوس" : r.canReadPlayer === false ? "إيصال التحصيل والاسترداد" : "ملف اللاعب والاسترداد"}</Link></article>)}</div><p className="muted">تُخصم من رصيد الوردية دون حذف إيصال التحصيل أو إعادة فتح دين.</p></section>}
    <section className="detail-card"><h2>حركات العهدة</h2><div className="linked-list">{data.movements.map(m => <article key={m.id}><strong>{m.kind === "TransferIn" ? "عهدة مستلمة" : "عهدة محولة"}</strong><span>{formatMoneyAr(m.amount, data.currency)}</span><small>{formatDateTimeAr(m.createdAtUtc)}</small></article>)}</div>{!data.movements.length && <p className="muted">لا توجد حركات للوردية. العهدة الافتتاحية محفوظة بشكل مستقل.</p>}</section>
  </>}</>;
}

type CloseRequest = { countedAmount: string; expectedAmount: string; reason: string | null };
const displayCash = (amount: string | undefined, currency?: string) => hasCashAmount(amount) ? formatMoneyAr(amount, currency) : "غير متاح";
type CloseStep = { kind: "review" | "retry"; request: CloseRequest }
  | { kind: "verify"; request: CloseRequest; outcome: "accepted" | "unknown" | "conflict" };

function CashReconciliation({ data, onRead, onDenied }: {
  data: SessionData; onRead: (next: SessionData) => void; onDenied: () => void;
}) {
  const id = data.session.id;
  const scope = `cash:/sessions/${id}/close`;
  const [step, setStep] = useState<CloseStep | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const running = useRef(false);

  function prepare(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (step || running.current) return;
    const fields = new FormData(event.currentTarget);
    const amount = canonicalDigits(String(fields.get("counted") ?? "").trim()).replace("٫", ".");
    if (!/^\d{1,9}(?:\.\d{1,2})?$/.test(amount) || Number(amount) > 999999999) {
      setError(`أدخل مبلغًا من صفر إلى ${formatMoneyAr(999999999, data.currency)}، بخانتين عشريتين على الأكثر.`);
      return;
    }
    const request = { countedAmount: Number(amount).toFixed(2), expectedAmount: data.balance, reason: String(fields.get("reason") ?? "").trim() || null };
    setError("");
    setStep({ kind: "review", request });
  }

  async function verify(request: CloseRequest, outcome: "accepted" | "unknown" | "conflict") {
    try {
      const next = await readSession(id);
      if (outcome === "accepted" && (!next.session.closedAtUtc || Number(next.session.expectedAmount) !== Number(request.expectedAmount) ||
        Number(next.session.countedAmount) !== Number(request.countedAmount) || (next.session.closingReason ?? null) !== request.reason)) {
        throw new Error();
      }
      onRead(next);
      if (next.session.closedAtUtc) {
        clearFinancialIntent(scope);
        return;
      }
      if (outcome === "unknown" && next.canClose && Number(next.balance) === Number(request.expectedAmount)) {
        setError("");
        setStep({ kind: "retry", request });
        return;
      }
      clearFinancialIntent(scope);
      setStep(null);
      setError("راجع الرصيد المحدّث والمبلغ المعدود قبل الإقفال.");
    } catch (caught) {
      if (inaccessible(caught)) { onDenied(); return; }
      setStep({ kind: "verify", request, outcome });
      setError("تعذر التحقق من حالة الوردية. أعد قراءة الوردية قبل أي محاولة إقفال أخرى.");
    }
  }

  async function proceed() {
    if (!step || running.current) return;
    running.current = true;
    setBusy(true);
    setError("");
    const { request } = step;
    try {
      if (step.kind === "verify") {
        await verify(request, step.outcome);
        return;
      }
      let outcome: "accepted" | "unknown" | "conflict" = "accepted";
      try {
        await csrfRequest(`${sessionPath(id)}/close`, "POST", request,
          { "Idempotency-Key": await pendingFinancialIntent(scope, request) }, { announce: false });
      } catch (caught) {
        if (inaccessible(caught)) { onDenied(); return; }
        if (caught instanceof DashboardRequestError && caught.status === 409) outcome = "conflict";
        else if (caught instanceof DashboardRequestError && caught.status >= 400 && caught.status < 500 && ![408, 429, 499].includes(caught.status)) {
          setStep(null);
          setError(caught.message);
          return;
        } else outcome = "unknown";
      }
      await verify(request, outcome);
    } finally {
      running.current = false;
      setBusy(false);
    }
  }

  const review = step?.kind === "review" ? step.request : null;
  const difference = review ? cashDifference(review.countedAmount, review.expectedAmount) : 0;
  return <section className="detail-card">
    <h2>إقفال الوردية</h2>
    <p>عدّ النقدية أولًا. يُحفظ الفرق للمراجعة من دون ترحيل إيراد أو مصروف.</p>
    {error && <ErrorState message={error} />}
    <form className="form-shell" noValidate onSubmit={prepare}>
      <label>المبلغ الموجود فعليًا<input name="counted" type="text" inputMode="decimal" dir="ltr" maxLength={12} required disabled={step !== null} /></label>
      <label>ملاحظة المطابقة (اختياري)<input name="reason" maxLength={400} disabled={step !== null} /></label>
      {step?.kind === "retry" && <p role="status">الوردية ما زالت مفتوحة. يمكنك إعادة إرسال المطابقة نفسها.</p>}
      <div className="form-actions">
        {step?.kind === "verify" ? <button className="secondary-button" type="button" disabled={busy} onClick={() => void proceed()}>إعادة قراءة الوردية</button>
          : step?.kind === "retry" ? <button className="primary-button" type="button" disabled={busy} onClick={() => void proceed()}>إعادة محاولة الإقفال</button>
          : <button className="primary-button" disabled={busy || step !== null}>مراجعة المطابقة</button>}
      </div>
    </form>
    <ConfirmationDialog open={review !== null} title="راجع المطابقة قبل الإقفال"
      message={review ? `الرصيد المتوقع ${formatMoneyAr(review.expectedAmount, data.currency)}، والمبلغ المعدود ${formatMoneyAr(review.countedAmount, data.currency)}. فرق المطابقة ${varianceLabel(difference, data.currency)}. تُحفظ المطابقة نهائيًا عند التأكيد.` : ""}
      confirmLabel="تأكيد إقفال الوردية" busy={busy} onCancel={() => setStep(null)} onConfirm={() => void proceed()} />
  </section>;
}
