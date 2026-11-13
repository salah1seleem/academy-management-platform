"use client";
import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { AdminDataTable, EmptyState, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { csrfRequest } from "./dashboard-api";
import { formatDateAr, formatDateTimeAr, formatMoneyAr } from "../lib/formatters";
import { useStaffAction } from "./use-staff-action";
import { clearFinancialIntent, pendingFinancialIntent } from "../lib/pending-financial-intent";
import styles from "./receivables.module.css";
type Balance = { id: string; player: string; branch: string; description: string; currency: string; charge: string; credits: string; allocated: string; outstanding: string; dueDate: string; overdue: boolean; source: string };
type Ledger = { asOf: string; currency: string; count: number; outstanding: string; overdue: string; notYetDue: string; overpaymentCredit: string; items: Balance[] };
type Option = { id: string; player: string; branch: string; sport: string };
const states = [{ id: "", label: "كل المطالبات" }, { id: "open", label: "قائمة" }, { id: "overdue", label: "متأخرة" }, { id: "future", label: "غير مستحقة بعد" }, { id: "settled", label: "مسددة" }, { id: "credit", label: "رصيد دائن" }];
const receivablesPath = "/dashboard/finance/receivables";
const receivablesApi = "/api/v1/finance/receivables";
function scopeFrom(search: { get(name: string): string | null }) {
  const state = search.get("state") ?? "";
  const branchId = search.get("branchId") ?? "";
  return {
    state: states.some(option => option.id === state) ? state : "",
    branchId: /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(branchId) ? branchId : "",
  };
}

function scopeQuery(state: string, branchId: string) {
  return new URLSearchParams({ ...(state ? { state } : {}), ...(branchId ? { branchId } : {}) });
}

function matchesState(row: Balance, state: string) {
  const amount = Number(row.outstanding);
  switch (state) {
    case "open": return amount > 0;
    case "overdue": return row.overdue;
    case "future": return amount > 0 && !row.overdue;
    case "settled": return amount === 0;
    case "credit": return amount < 0;
    default: return true;
  }
}

function balanceLabel(value: string) {
  const amount = Number(value);
  return amount < 0 ? "رصيد دائن" : amount === 0 ? "مسددة" : "الرصيد القائم";
}

function balanceMoney(value: string, currency: string) {
  return formatMoneyAr(Math.abs(Number(value)), currency);
}

export function Receivables() {
  const { state, branchId } = scopeFrom(useSearchParams());
  const canAdd = useStaffAction("finance", "add");
  const [canExport, setCanExport] = useState(false);
  const [result, setResult] = useState<{ branchId: string; data?: Ledger; error?: string }>();
  const [revision, setRevision] = useState(0);
  const [branches, setBranches] = useState<{ id: string; arabicName: string }[]>([]);
  const [search, setSearch] = useState("");
  const [lookup, setLookup] = useState<{ search: string; options?: Option[] }>();
  const [enrollment, setEnrollment] = useState("");
  const [create, setCreate] = useState(false);
  const [busy, setBusy] = useState(false);
  const [formError, setFormError] = useState("");
  const searchField = useRef<HTMLInputElement>(null);
  const createButton = useRef<HTMLButtonElement>(null);
  const current = result?.branchId === branchId ? result : undefined;
  const data = current?.data;
  const options = lookup?.search === search ? lookup : undefined;
  const selectScope = (nextState: string, nextBranch: string) => {
    const query = scopeQuery(nextState, nextBranch).toString();
    window.history.replaceState(null, "", query ? `${receivablesPath}?${query}` : receivablesPath);
  };
  const refresh = () => { setResult(undefined); setRevision(value => value + 1); };
  useEffect(() => {
    const controller = new AbortController();
    async function load() {
      try {
        const response = await fetch(`${receivablesApi}?${scopeQuery("", branchId)}`, { cache: "no-store", signal: controller.signal });
        if (!response.ok) throw Error();
        const value: Ledger = await response.json();
        if (!validReceivableLedger(value)) throw Error();
        if (!controller.signal.aborted) setResult({ branchId, data: value });
      } catch {
        if (!controller.signal.aborted) setResult({ branchId, error: "تعذر تحميل المطالبات في هذا النطاق." });
      }
    }
    void load();
    return () => controller.abort();
  }, [branchId, revision]);

  useEffect(() => {
    const controller = new AbortController();
    const signal = controller.signal;
    void fetch("/api/v1/manage/structure/options", { signal }).then(r => r.ok ? r.json() : null)
      .then(o => { if (!signal.aborted && Array.isArray(o?.branches)) setBranches(o.branches); }).catch(() => {});
    void fetch("/api/v1/my-permissions", { signal }).then(r => r.ok ? r.json() : null)
      .then(p => { if (!signal.aborted) setCanExport(Array.isArray(p) && p.includes("reports.export")); }).catch(() => {});
    return () => controller.abort();
  }, []);

  useEffect(() => {
    if (!create) return;
    const controller = new AbortController();
    void fetch(`${receivablesApi}/options?search=${encodeURIComponent(search)}`, { signal: controller.signal })
      .then(async response => {
        if (!response.ok) throw Error();
        const value = await response.json();
        if (!Array.isArray(value?.enrollments)) throw Error();
        if (!controller.signal.aborted) setLookup({ search, options: value.enrollments });
      })
      .catch(() => { if (!controller.signal.aborted) setLookup({ search }); });
    return () => controller.abort();
  }, [create, search]);

  useEffect(() => {
    if (create) searchField.current?.focus();
  }, [create]);

  function closeForm() {
    setCreate(false);
    requestAnimationFrame(() => createButton.current?.focus());
  }

  function openForm() {
    setSearch(""); setLookup(undefined); setEnrollment(""); setFormError(""); setCreate(true);
  }

  async function submit(form: HTMLFormElement) {
    if (busy || !options?.options?.some(option => option.id === enrollment)) return;
    setBusy(true); setFormError("");
    const f = new FormData(form);
    const body = { sportEnrollmentId: enrollment, planId: null, amount: f.get("amount"), dueDate: f.get("dueDate"), source: f.get("source"), description: f.get("description") };
    try {
      await csrfRequest(receivablesApi, "POST", body, { "Idempotency-Key": await pendingFinancialIntent("charge", body) });
      clearFinancialIntent("charge"); closeForm(); refresh();
    } catch (e) {
      setFormError(e instanceof Error ? e.message : "تعذر تثبيت المطالبة.");
    } finally {
      setBusy(false);
    }
  }

  const visible = data?.items.filter(row => matchesState(row, state)) ?? [];
  const query = scopeQuery(state, branchId);
  const detailHref = (id: string) => `${receivablesPath}/${id}${query.size ? `?${query}` : ""}`;
  return <>
    <PageHeader title="المطالبات والأرصدة" context="المالية" description="الأرصدة من المطالبات المثبتة وحركاتها. انتهاء الاشتراك وحده لا ينشئ مطالبة." />
    {canAdd && create && <section className="detail-card">
      <h2>إضافة مطالبة</h2>
      <p className="muted">تُضاف المطالبة إلى سجل المستحقات. تفعيل الاشتراك يتم من مسار الاشتراكات.</p>
      {formError && <ErrorState message={formError} />}
      <form className="form-shell" onSubmit={e => { e.preventDefault(); void submit(e.currentTarget); }}>
        <label>البحث عن اللاعب<input ref={searchField} value={search} disabled={busy} onChange={e => { setSearch(e.target.value); setLookup(undefined); setEnrollment(""); }} placeholder="الاسم أو كود اللاعب" /></label>
        <label>تسجيل اللاعب<select name="enrollment" value={enrollment} onChange={e => setEnrollment(e.target.value)} required disabled={busy || !options?.options?.length}><option value="">اختر تسجيلًا</option>{options?.options?.map(x => <option key={x.id} value={x.id}>{x.player} · {x.sport} · {x.branch}</option>)}</select></label>
        {!options ? <p role="status">جارٍ البحث عن التسجيلات…</p>
          : !options.options ? <p role="status">تعذر البحث عن التسجيلات. غيّر عبارة البحث للمحاولة مجددًا.</p>
          : !options.options.length && <p role="status">لا توجد تسجيلات مطابقة.</p>}
        <label>{data?.currency === "EGP" ? "المبلغ بالجنيه" : `المبلغ ${data?.currency ?? ""}`}<input name="amount" type="number" inputMode="decimal" min="0.01" max="999999999" step="0.01" required /></label>
        <label>تاريخ الاستحقاق<input name="dueDate" type="date" required /></label>
        <label>المصدر<select name="source"><option value="ManualCharge">مطالبة معتمدة</option><option value="OpeningBalance">رصيد افتتاحي موثق · لا يسجل تحصيلًا اليوم</option></select></label>
        <label>الوصف<input name="description" maxLength={400} required /></label>
        <div className="form-actions"><button className="primary-button" disabled={busy || !enrollment || !data}>تثبيت المطالبة</button><button type="button" className="secondary-button" disabled={busy} onClick={closeForm}>إلغاء</button></div>
      </form>
    </section>}
    <section className="list-card" aria-label="المطالبات المالية">
      <div className={styles.filters}>
        <label>حالة الرصيد<select value={state} onChange={e => selectScope(e.target.value, branchId)}>{states.map(s => <option key={s.id} value={s.id}>{s.label}</option>)}</select></label>
        <label>الفرع<select value={branchId} onChange={e => selectScope(state, e.target.value)}><option value="">كل الفروع</option>{branches.map(b => <option key={b.id} value={b.id}>{b.arabicName}</option>)}{branchId && !branches.some(b => b.id === branchId) && <option value={branchId}>الفرع المحدد</option>}</select></label>
        {((canAdd && !create) || state || branchId) && <div className={styles.filterActions}>
          {canAdd && !create && <button className="primary-button" type="button" ref={createButton} onClick={openForm}>إضافة مطالبة</button>}
          {(state || branchId) && <button type="button" className="secondary-button" onClick={() => selectScope("", "")}>مسح الفلتر</button>}
        </div>}
      </div>
      {current?.error ? <><ErrorState message={current.error} /><button type="button" className="secondary-button" onClick={refresh}>إعادة المحاولة</button></>
        : !data ? <LoadingState /> : <>
          <h2 className={styles.balanceScope}>{branchId ? "ملخص رصيد الفرع" : "ملخص رصيد كل الفروع"}</h2>
          <div className={`receivable-metrics ${styles.metrics}`} role="group" aria-label={branchId ? "أرصدة الفرع" : "أرصدة كل الفروع"}>
            {[
              { value: data.outstanding, label: "الرصيد القائم", state: "open", tone: "blue" },
              { value: data.overdue, label: "المتأخر المستحق", state: "overdue", tone: "rose" },
              { value: data.notYetDue, label: "غير مستحق بعد", state: "future", tone: "gold" },
              { value: data.overpaymentCredit, label: "رصيد دائن للأسرة", state: "credit", tone: "green" },
            ].map(x => <button type="button" key={x.state} className={`receivable-metric ${x.tone}`} aria-pressed={state === x.state} onClick={() => selectScope(x.state, branchId)}><span>{x.label}</span><strong>{formatMoneyAr(x.value, data.currency)}</strong></button>)}
          </div>
          <div className={styles.tools}>
            <p role="status"><span className="sr-only">عدد المطالبات {visible.length} · </span>رصيد حتى {formatDateTimeAr(data.asOf)}</p>
            <div>
              <button type="button" className="secondary-button" onClick={refresh}>تحديث الأرصدة</button>
              {canExport && <a className="secondary-button" href={`${receivablesApi}/export?${new URLSearchParams({ ...Object.fromEntries(query), asOf: data.asOf })}`}>تصدير CSV</a>}
            </div>
          </div>
          {visible.length === 0 ? <EmptyState message={state ? "لا توجد مطالبات بهذه الحالة في النطاق المحدد." : "لا توجد مطالبات في هذا النطاق."} /> : <AdminDataTable key={`${branchId}:${state}`} label="المطالبات المالية" rows={visible} rowKey={r => r.id} paginate={visible.length > 25} columns={[
            { key: "player", header: "اللاعب", primary: true, render: r => <><Link href={detailHref(r.id)}><b>{r.player}</b></Link><small>{r.description}</small></> },
            { key: "branch", header: "الفرع", render: r => r.branch },
            { key: "due", header: "الاستحقاق", render: r => formatDateAr(r.dueDate) },
            { key: "amount", header: "أصل المطالبة", render: r => formatMoneyAr(r.charge, r.currency) },
            { key: "balance", header: "الرصيد", render: r => <><b className={r.overdue ? "text-danger" : ""}>{balanceMoney(r.outstanding, r.currency)}</b><small>{r.overdue ? "متأخر" : balanceLabel(r.outstanding)}</small></> },
          ]} />}
        </>}
    </section>
  </>;
}
type Document = { id: string; playerNameSnapshot?: string; description: string; amount: string; currency: string; dueDate: string; planNameSnapshot?: string; planPriceSnapshot?: string; source: string };
type Movement = { id: string; kind: string; amount: string; reason: string; createdAtUtc: string };
type CreditApplication = { id: string; sourceDocumentId: string; targetDocumentId: string; amount: string; reason: string; createdAtUtc: string };
type Detail = { asOf?: string; document: Document; balance?: Balance; movements: Movement[]; canUseCredit?: boolean; creditApplications?: CreditApplication[] };
type CollectionOption = { id: string; available: string; confirmedAtUtc: string };

export function ReceivableDetail({ id }: { id: string }) {
  const { state, branchId } = scopeFrom(useSearchParams());
  const query = scopeQuery(state, branchId);
  const back = query.size ? `${receivablesPath}?${query}` : receivablesPath;
  return <ScopedReceivableDetail key={id} id={id} back={back} />;
}

function ScopedReceivableDetail({ id, back }: { id: string; back: string }) {
  const canAdd = useStaffAction("finance", "add");
  const router = useRouter();
  const path = `${receivablesApi}/${id}`;
  const [result, setResult] = useState<{ data?: Detail; error?: string; retry?: boolean }>();
  const [revision, setRevision] = useState(0);
  const [lookup, setLookup] = useState<{ items?: CollectionOption[] }>();
  const [lookupRevision, setLookupRevision] = useState(0);
  const [kind, setKind] = useState("CreditNote");
  const [collectionId, setCollectionId] = useState("");
  const [saveError, setSaveError] = useState("");
  const [busy, setBusy] = useState(false);
  const data = result?.data;
  const payment = canAdd && kind === "PaymentAllocation" && Boolean(data);
  useEffect(() => {
    const controller = new AbortController();
    void fetch(path, { cache: "no-store", signal: controller.signal }).then(async response => {
      if (!response.ok) {
        if (!controller.signal.aborted && [401, 403, 404].includes(response.status)) {
          setResult({ error: "هذه المطالبة غير متاحة في حسابك الحالي." });
          return;
        }
        throw Error();
      }
      const value: Detail = await response.json();
      if (controller.signal.aborted) return;
      if (value.document?.id !== id || value.balance && value.balance.id !== id) {
        setResult({ error: "لم تتأكد هوية مستند المستحقات. أعد فتحه من القائمة." });
        return;
      }
      if (!Array.isArray(value.movements)) throw Error();
      setResult({ data: value });
    }).catch(() => {
      if (!controller.signal.aborted) setResult({ error: "تعذر تحميل المطالبة.", retry: true });
    });
    return () => controller.abort();
  }, [id, path, revision]);

  useEffect(() => {
    if (!payment) return;
    const controller = new AbortController();
    void fetch(`${path}/collections`, { cache: "no-store", signal: controller.signal }).then(async response => {
      if (!response.ok) throw Error();
      const items: CollectionOption[] = await response.json();
      if (!Array.isArray(items)) throw Error();
      if (!controller.signal.aborted) setLookup({ items });
    }).catch(() => {
      if (!controller.signal.aborted) setLookup({});
    });
    return () => controller.abort();
  }, [lookupRevision, path, payment]);

  async function save(form: HTMLFormElement) {
    if (busy || !canAdd || payment && !lookup?.items?.some(item => item.id === collectionId)) return;
    setBusy(true); setSaveError("");
    const f = new FormData(form);
    const body = { kind, amount: f.get("amount"), collectionId: payment ? collectionId : null, reason: f.get("reason") };
    const scope = `movement:${id}`;
    try {
      await csrfRequest(`${path}/movements`, "POST", body, { "Idempotency-Key": await pendingFinancialIntent(scope, body) });
      clearFinancialIntent(scope);
      router.push(back);
    } catch (e) {
      setSaveError(e instanceof Error ? e.message : "تعذر تثبيت الحركة.");
    } finally {
      setBusy(false);
    }
  }
  const applications = data?.creditApplications ?? [];
  const events = data ? [
    ...data.movements.map(m => ({
      ...m, label: m.kind === "CreditNote" ? "إشعار دائن" : "تخصيص تحصيل", href: null,
    })),
    ...applications.map(a => ({
      ...a, label: a.sourceDocumentId === id ? "رصيد دائن مستخدم" : "تسوية برصيد دائن",
      href: `${receivablesPath}/${a.sourceDocumentId === id ? a.targetDocumentId : a.sourceDocumentId}${back.slice(receivablesPath.length)}`,
    })),
  ].sort((a, b) => Date.parse(b.createdAtUtc) - Date.parse(a.createdAtUtc)) : [];
  return <>
    <PageHeader title="تفاصيل المطالبة" context="المالية / المطالبات" back={{ label: "العودة إلى المطالبات", href: back }} />
    {result?.error ? <>
      <ErrorState message={result.error} />
      {result.retry && <button type="button" className="secondary-button" onClick={() => { setResult(undefined); setRevision(value => value + 1); }}>إعادة المحاولة</button>}
    </> : !data ? <LoadingState /> : <>
      <section className="detail-card">
        <h2>{data.document.description}</h2>
        {data.document.playerNameSnapshot && <p>{data.document.playerNameSnapshot}{data.balance?.branch && ` · ${data.balance.branch}`}</p>}
        {data.balance && <div className={styles.documentBalance}>
          <h3>{Number(data.balance.outstanding) < 0 ? "رصيد لصالح العميل" : balanceLabel(data.balance.outstanding)}</h3>
          <strong>{balanceMoney(data.balance.outstanding, data.balance.currency)}</strong>
          {data.asOf && <small>رصيد حتى {formatDateTimeAr(data.asOf)}</small>}
        </div>}
        <div className={styles.documentFacts}>
          <p>أصل المطالبة: {formatMoneyAr(data.document.amount, data.document.currency)}</p>
          <p>الاستحقاق: {formatDateAr(data.document.dueDate)}</p>
          {data.balance && <p>تحصيل مخصص: {formatMoneyAr(data.balance.allocated, data.balance.currency)} · إشعار دائن: {formatMoneyAr(data.balance.credits, data.balance.currency)}</p>}
          {data.document.planNameSnapshot && <p>الباقة عند تثبيت المطالبة {data.document.planNameSnapshot}{data.document.planPriceSnapshot != null && ` · ${formatMoneyAr(data.document.planPriceSnapshot, data.document.currency)}`}</p>}
        </div>
      </section>
      {data.canUseCredit && <div className="form-actions"><Link className="primary-button" href={`${receivablesPath}/${id}/credit${back.slice(receivablesPath.length)}`}>استخدام الرصيد الدائن</Link></div>}
      {canAdd && <section className="detail-card">
        <h2>إضافة حركة موثقة</h2>
        <p className="muted">مستند مثبت لا يُعدل ولا يُحذف. أضف إشعارًا دائنًا لتصحيح المبلغ، أو خصّص تحصيلًا مؤكدًا لتسوية الرصيد.</p>
        {saveError && <ErrorState message={saveError} />}
        <form className="form-shell" onSubmit={e => { e.preventDefault(); void save(e.currentTarget); }}>
          <label>نوع الحركة<select value={kind} disabled={busy} onChange={e => { setKind(e.target.value); setLookup(undefined); setCollectionId(""); }}><option value="CreditNote">إشعار دائن لتخفيض المطالبة</option><option value="PaymentAllocation">تخصيص من تحصيل مؤكد</option></select></label>
          {payment && <>
            {!lookup ? <p role="status">جارٍ تحميل التحصيلات المتاحة…</p>
              : !lookup.items ? <><ErrorState message="تعذر تحميل التحصيلات المتاحة." /><button type="button" className="secondary-button" onClick={() => { setLookup(undefined); setLookupRevision(value => value + 1); }}>إعادة تحميل التحصيلات</button></>
              : !lookup.items.length ? <p role="status">لا يوجد تحصيل مؤكد متاح للتخصيص.</p>
              : <label>التحصيل المتاح<select name="collection" value={collectionId} disabled={busy} onChange={e => setCollectionId(e.target.value)} required><option value="">اختر تحصيلًا</option>{lookup.items.map(c => <option key={c.id} value={c.id}>{formatDateTimeAr(c.confirmedAtUtc)} · متاح {formatMoneyAr(c.available, data.document.currency)}</option>)}</select></label>}
          </>}
          <label>المبلغ<input name="amount" type="number" inputMode="decimal" min="0.01" max="999999999" step="0.01" required /></label>
          <label>سبب الحركة<input name="reason" maxLength={400} required /></label>
          <button className="primary-button" disabled={busy || payment && !collectionId}>تثبيت الحركة</button>
        </form>
      </section>}
      <section className="detail-card"><h2>التاريخ المالي</h2>
        {events.length === 0 ? <EmptyState message="لم تُسجل حركات على المطالبة." /> : events.map(event => <article className="payment-queue-row" key={event.id}><div><b>{event.label}</b><small>{event.reason} · {formatDateTimeAr(event.createdAtUtc)}</small>{event.href && <Link href={event.href}>عرض المطالبة المرتبطة</Link>}</div><strong>{formatMoneyAr(event.amount, data.document.currency)}</strong></article>)}
      </section>
    </>}
  </>;
}
export function GuardianReceivables() {
  const [result, setResult] = useState<Ledger | "unavailable" | "failed" | null>(null);
  const [revision, setRevision] = useState(0);
  useEffect(() => {
    const controller = new AbortController();
    const { signal } = controller;
    void fetch("/api/v1/guardian/receivables", { cache: "no-store", signal }).then(async response => {
      if ([401, 403, 404].includes(response.status)) {
        if (!signal.aborted) setResult("unavailable");
        return;
      }
      if (!response.ok) throw new Error();
      const value: Ledger = await response.json();
      if (!validReceivableLedger(value)) throw new Error();
      if (!signal.aborted) setResult(value);
    }).catch(() => { if (!signal.aborted) setResult("failed"); });
    return () => controller.abort();
  }, [revision]);

  if (result === "unavailable") return <section className="profile-section"><EmptyState message="أرصدة الأسرة غير متاحة لهذا الحساب." /></section>;
  if (result === "failed") return <section className="profile-section">
    <ErrorState message="تعذر تحميل أرصدة الأسرة." />
    <button type="button" className="secondary-button" onClick={() => { setResult(null); setRevision(value => value + 1); }}>إعادة المحاولة</button>
  </section>;
  if (!result) return <section className="profile-section"><LoadingState message="جارٍ تحميل أرصدة الأسرة…" /></section>;

  const data = result;
  const balances = [
    { label: "إجمالي المستحقات", value: data.outstanding },
    { label: "المتأخر", value: data.overdue },
    { label: "غير مستحق بعد", value: data.notYetDue },
    ...(Number(data.overpaymentCredit) > 0 ? [{ label: "رصيد دائن للأسرة", value: data.overpaymentCredit }] : []),
  ];
  return <>
    <section className="profile-section" aria-label="ملخص الرصيد">
      <dl className={styles.familyTotals}>{balances.map(({ label, value }) => <div key={label}>
        <dt>{label}</dt><dd>{formatMoneyAr(value, data.currency)}</dd>
      </div>)}</dl>
      <small>الرصيد حتى {formatDateTimeAr(data.asOf)}</small>
    </section>
    <section className="profile-section" aria-labelledby="guardian-posted-balances">
      <h2 id="guardian-posted-balances">المطالبات المثبتة</h2>
      {data.items.length === 0 ? <EmptyState message="لا توجد مطالبات مثبتة للأسرة." /> :
        <ul className={styles.familyClaims}>{data.items.map(row => <li key={row.id}>
          <h3>{row.player}</h3>
          {row.description && <p>{row.description}</p>}
          <p>{Number(row.outstanding) === 0 ? "أُقفل الرصيد" : `${balanceLabel(row.outstanding)} ${balanceMoney(row.outstanding, row.currency)}`}</p>
          <small>أصل المطالبة {formatMoneyAr(row.charge, row.currency)}{Number(row.outstanding) > 0 && ` · الاستحقاق ${formatDateAr(row.dueDate)}${row.overdue ? " · متأخرة" : ""}`}</small>
        </li>)}</ul>}
    </section>
  </>;
}

function postedAmount(value: unknown) {
  return typeof value === "string" && /^-?\d+(?:\.\d{1,2})?$/.test(value) && Number.isFinite(Number(value));
}

function validReceivableLedger(value: Ledger) {
  return Array.isArray(value?.items) && Number.isFinite(Date.parse(value.asOf)) &&
    /^[A-Z]{3}$/.test(value.currency) &&
    [value.outstanding, value.overdue, value.notYetDue, value.overpaymentCredit].every(postedAmount) &&
    value.items.every(row => typeof row?.id === "string" && row.id.trim() && typeof row.player === "string" &&
      typeof row.overdue === "boolean" && /^[A-Z]{3}$/.test(row.currency) && postedAmount(row.outstanding) && postedAmount(row.charge));
}
