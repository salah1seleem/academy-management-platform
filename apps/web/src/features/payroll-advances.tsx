"use client";

import { type FormEvent, useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { ConfirmationDialog, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { apiProblemMessage } from "../lib/api-problem";
import { formatDateAr, formatMoneyAr } from "../lib/formatters";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";

type Advance = { id: string; amount: number; applied: number; remaining: number; reference?: string; createdAtUtc: string };
type Statement = { month: string; kind?: string; openingAsOf?: string; netDue: number; remaining: number };
type Data = {
  employeeId: string; employeeName: string; currency: string; timeZone: string; recoveryScope: string;
  canManage: boolean; canRecord: boolean; advances: Advance[]; statements: Statement[];
  applications: { id: string; advanceId: string; month: string; amount: number; kind?: string }[];
};
type Command =
  | { kind: "record"; body: { amount: number; reference: string } }
  | { kind: "apply"; advanceId: string; body: { month: string; amount: number; expectedAdvanceRemaining: number; expectedStatementRemaining: number; opening?: true } };
type Review = Command & { message: string };
type Attempt = Command & { key: string; acceptedId?: string; retryAllowed?: boolean };

const storageError = "تعذر استعادة محاولة السلفة من هذا التبويب. راجع سجل السلف قبل تسجيل عملية جديدة.";
const readError = "تعذر تحميل السلف. أعد المحاولة.";
const deniedMessage = "سلف هذا الموظف غير متاحة لهذا الحساب.";
const denied = (error: unknown) => error instanceof DashboardRequestError && [401, 403, 404].includes(error.status);
const validBalance = (value: number) => Number.isFinite(value) && value > 0 && Number(value.toFixed(2)) === value;
const validAmount = (value: number) => validBalance(value) && value <= 100000000;

function savedAttempt(slot: string): Attempt | null {
  const raw = sessionStorage.getItem(slot);
  if (raw === null) return null;
  const { key, kind, advanceId, body } = JSON.parse(raw);
  if (typeof key !== "string" || !/^[a-f0-9-]{36}$/i.test(key) || !validAmount(body?.amount)) throw new Error();
  const amount = body.amount as number;
  if (kind === "record" && typeof body.reference === "string" && body.reference.length <= 120) {
    return { key, kind, body: { amount, reference: body.reference } };
  }
  if (kind === "apply" && typeof advanceId === "string" && /^[a-f0-9-]{36}$/i.test(advanceId) &&
      typeof body.month === "string" && /^\d{4}-\d{2}-01$/.test(body.month) &&
      validBalance(body.expectedAdvanceRemaining) && validBalance(body.expectedStatementRemaining) &&
      amount <= Math.min(body.expectedAdvanceRemaining, body.expectedStatementRemaining) && (body.opening === undefined || body.opening === true)) {
    return { key, kind, advanceId, body: { month: body.month, amount, expectedAdvanceRemaining: body.expectedAdvanceRemaining, expectedStatementRemaining: body.expectedStatementRemaining, ...(body.opening ? { opening: true } : {}) } };
  }
  throw new Error();
}

async function readId(response: Response) {
  const value = await response.json();
  if (typeof value?.id !== "string" || !value.id) throw new Error();
  return value.id as string;
}

export function PayrollAdvances({ id }: { id: string }) {
  return <AdvancesScreen key={id} id={id} />;
}

function AdvancesScreen({ id }: { id: string }) {
  const path = `/api/v1/manage/payroll/${id}/advances`;
  const slotFor = useCallback((scope: string) => `academy-payroll-advance:${scope}:${id}`, [id]);
  const [data, setData] = useState<Data | null>(null);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const [reloadRequired, setReloadRequired] = useState(false);
  const [review, setReview] = useState<Review | null>(null);
  const [attempt, setAttempt] = useState<Attempt | null>(null);
  const running = useRef(false);

  const read = useCallback(async (signal?: AbortSignal, scope?: string) => {
    const response = await fetch(path, { cache: "no-store", signal, ...(scope ? { headers: { "X-Payroll-Scope": scope } } : {}) });
    if (!response.ok) throw new DashboardRequestError(await apiProblemMessage(response, [401, 403, 404].includes(response.status) ? deniedMessage : readError), response.status);
    const value: Data = await response.json();
    if (scope && scope !== value.recoveryScope) throw new DashboardRequestError(deniedMessage, 403);
    if (value.employeeId !== id || typeof value.recoveryScope !== "string" || !/^[a-f0-9]{64}$/i.test(value.recoveryScope) ||
        !Array.isArray(value.advances) || !Array.isArray(value.statements) || !Array.isArray(value.applications)) throw new Error();
    return value;
  }, [id, path]);

  const open = useCallback((value: Data) => {
    setData(value);
    try {
      setAttempt(savedAttempt(slotFor(value.recoveryScope)));
      setReloadRequired(false);
      setError("");
    } catch {
      setAttempt(null);
      setReloadRequired(true);
      setError(storageError);
    }
  }, [slotFor]);

  function readFailed(cause: unknown, fallback: string) {
    setReloadRequired(true);
    if (denied(cause)) {
      setData(null);
      setAttempt(null);
      setNotice("");
      setError(deniedMessage);
    } else {
      setError(fallback);
    }
  }

  useEffect(() => {
    const controller = new AbortController();
    void read(controller.signal).then(value => {
      if (!controller.signal.aborted) open(value);
    }).catch(cause => {
      if (!controller.signal.aborted) readFailed(cause, readError);
    });
    return () => controller.abort();
  }, [open, read]);

  async function run(work: () => Promise<void>) {
    if (running.current) return;
    running.current = true;
    setBusy(true);
    setError("");
    try { await work(); }
    finally { running.current = false; setBusy(false); }
  }

  function prepare(event: FormEvent<HTMLFormElement>, kind: Command["kind"]) {
    event.preventDefault();
    if (!data || running.current || attempt || reloadRequired) return;
    const fields = new FormData(event.currentTarget);
    const amount = Number(fields.get("amount"));
    setError("");
    setNotice("");
    if (!validAmount(amount)) { setError("أدخل مبلغًا موجبًا بخانتين عشريتين على الأكثر."); return; }
    const formatted = formatMoneyAr(amount, data.currency);
    if (kind === "record") {
      setReview({ kind, body: { amount, reference: String(fields.get("reference") ?? "").trim() }, message: `${data.employeeName} · سلفة مصروفة ${formatted}. تأكد أن الموظف تسلّم المبلغ. لا ينفذ النظام تحويلًا بنكيًا ولا يخصم السلفة تلقائيًا.` });
      return;
    }
    const advance = advances.find(a => a.id === fields.get("advanceId"));
    const statement = data.statements.find(s => s.month === fields.get("month"));
    if (!advance || !statement || amount > Math.min(advance.remaining, statement.remaining)) {
      setError("راجع السلفة والكشف والمبلغ المتاح.");
      return;
    }
    const opening = statement.kind === "Opening";
    const destination = opening ? "الافتتاح الموثق" : `كشف ${formatDateAr(statement.month)}`;
    setReview({
      kind, advanceId: advance.id,
      body: { month: statement.month, amount, expectedAdvanceRemaining: advance.remaining, expectedStatementRemaining: statement.remaining, ...(opening ? { opening: true } : {}) },
      message: `${data.employeeName} · تخصيص ${formatted} من سلفة ${advance.number} إلى ${destination}. هذا يسدد جزءًا من المستحق من مبلغ سبق صرفه. ليس صرفًا جديدًا أو خصمًا من الراتب الأساسي.`,
    });
  }

  function clearAttempt(command: Attempt, scope: string) {
    const slot = slotFor(scope);
    if (savedAttempt(slot)?.key === command.key) sessionStorage.removeItem(slot);
    setAttempt(null);
  }

  async function showSaved(command: Attempt, scope: string, acceptedId: string) {
    setAttempt({ ...command, acceptedId });
    try {
      const current = await read(undefined, scope);
      const rows = command.kind === "record" ? current.advances : current.applications;
      if (!rows.some(row => row.id === acceptedId)) throw new Error();
      clearAttempt(command, scope);
      setData(current);
      setReloadRequired(false);
      setNotice(command.kind === "record" ? "تم تسجيل السلفة المصروفة دون خصم تلقائي." : "تم تخصيص السلفة دون صرف جديد أو خصم إضافي.");
    } catch (cause) {
      readFailed(cause, "حُفظ الطلب وتعذر تحديث الأرصدة. أعد القراءة قبل تسجيل عملية جديدة.");
    }
  }

  async function send(command: Attempt) {
    if (!data) return;
    const scope = data.recoveryScope;
    await run(async () => {
      setNotice("");
      setReview(null);
      try {
        const slot = slotFor(scope);
        const saved = savedAttempt(slot);
        if (saved && saved.key !== command.key) {
          setAttempt(saved);
          setError("تحقّق من المحاولة السابقة قبل تسجيل عملية جديدة.");
          return;
        }
        if (!saved) sessionStorage.setItem(slot, JSON.stringify(command));
      } catch {
        setReloadRequired(true);
        setError(storageError);
        return;
      }
      setAttempt({ ...command, retryAllowed: false });
      try {
        const endpoint = command.kind === "record" ? path : `${path}/${encodeURIComponent(command.advanceId)}/applications`;
        const response = await csrfRequest(endpoint, "POST", command.body, { "Idempotency-Key": command.key, "X-Payroll-Scope": scope }, { announce: false });
        await showSaved(command, scope, await readId(response));
      } catch (cause) {
        if (denied(cause)) { readFailed(cause, deniedMessage); return; }
        if (cause instanceof DashboardRequestError && [400, 409, 422].includes(cause.status)) {
          try { clearAttempt(command, scope); }
          catch { setReloadRequired(true); setError(storageError); return; }
          setError(cause.message);
          if (cause.status === 409) {
            try { setData(await read(undefined, scope)); setReloadRequired(false); }
            catch (readCause) { readFailed(readCause, "تغيّرت الأرصدة. أعد القراءة قبل المراجعة."); }
          }
          return;
        }
        setError("تعذر تأكيد الحفظ. تحقّق من نتيجة المحاولة قبل تسجيل عملية جديدة.");
      }
    });
  }

  function verify() {
    void run(async () => {
      if (!attempt || !data) {
        try { open(await read()); }
        catch (cause) { readFailed(cause, readError); }
        return;
      }
      const scope = data.recoveryScope;
      if (attempt.acceptedId) { await showSaved(attempt, scope, attempt.acceptedId); return; }
      try {
        const query = new URLSearchParams({ kind: attempt.kind, requestKey: attempt.key });
        const response = await fetch(`${path}/by-request?${query}`, { cache: "no-store", headers: { "X-Payroll-Scope": scope } });
        if (response.status === 404) {
          setData(await read(undefined, scope));
          setAttempt({ ...attempt, retryAllowed: true });
          setError("لم تظهر نتيجة مؤكدة. يمكنك إعادة إرسال المحاولة نفسها.");
          return;
        }
        if (!response.ok) throw new DashboardRequestError(readError, response.status);
        await showSaved(attempt, scope, await readId(response));
      } catch (cause) { readFailed(cause, "تعذر التحقق من المحاولة. أعد المحاولة."); }
    });
  }

  const canAct = !attempt && !reloadRequired;
  const advances = data?.advances.map((advance, index) => ({ ...advance, number: data.advances.length - index })) ?? [];
  const availableAdvances = advances.filter(a => a.remaining > 0);
  const statements = data?.statements.filter(s => s.remaining > 0) ?? [];
  const money = (amount: number) => formatMoneyAr(amount, data?.currency);
  const date = new Intl.DateTimeFormat("ar-EG-u-nu-latn", { dateStyle: "long", timeZone: data?.timeZone ?? "Africa/Cairo" });
  return <>
    <PageHeader title="سلف الموظف وتخصيصها" context={data?.employeeName ?? "الرواتب"} secondaryActions={data ? [{ label: "حساب الموظف", href: `/dashboard/payroll/${id}/account` }] : []} />
    {data && <p className="muted">{data.employeeName}</p>}
    {error && <ErrorState message={error} />}
    {notice && <p role="status" className="success-note">{notice}</p>}
    {!data && !error && <LoadingState />}
    {attempt && data ? <section className="detail-card" aria-labelledby="advance-attempt-title" aria-busy={busy}>
      <h2 id="advance-attempt-title">{attempt.acceptedId ? "تحديث الأرصدة بعد الحفظ" : "محاولة سلفة بانتظار التحقق"}</h2>
      <div className="form-actions">
        <button className="primary-button" disabled={busy} onClick={verify}>{attempt.acceptedId ? "إعادة قراءة الأرصدة" : attempt.kind === "record" ? "التحقق من تسجيل السلفة" : "التحقق من تخصيص السلفة"}</button>
        {attempt.retryAllowed && !attempt.acceptedId && <button className="secondary-button" disabled={busy} onClick={() => void send(attempt)}>إعادة إرسال المحاولة نفسها</button>}
      </div>
    </section> : reloadRequired && <button className="secondary-button" disabled={busy} onClick={verify}>إعادة قراءة السلف</button>}
    {data && <>
      <section className="list-card statement-details"><h2>السلف المسجلة</h2>
        {!advances.length ? <p>لا سلف مسجلة في هذا المسار.</p> : advances.map(a => <article className="statement-payment" key={a.id}>
          <strong>{money(a.amount)}</strong><span>المخصص {money(a.applied)} · غير المخصص {money(a.remaining)}</span><small>سلفة {a.number} · {date.format(new Date(a.createdAtUtc))}{a.reference ? ` · ${a.reference}` : ""}</small>
        </article>)}
      </section>
      {canAct && data.canRecord && <section className="detail-card"><h2>تسجيل سلفة مصروفة</h2>
        <form className="form-shell" aria-label="تسجيل سلفة" onSubmit={event => prepare(event, "record")}>
          <label>مبلغ السلفة<input name="amount" type="number" min="0.01" max="100000000" step="0.01" required /></label>
          <label>مرجع السلفة (اختياري)<input name="reference" maxLength={120} /></label>
          <button className="primary-button" disabled={busy}>مراجعة السلفة</button>
        </form>
      </section>}
      {canAct && data.canManage && availableAdvances.length > 0 && <section className="detail-card"><h2>تخصيص إلى كشف معتمد</h2>
        {statements.length ? <form className="form-shell" aria-label="تخصيص السلفة" onSubmit={event => prepare(event, "apply")}>
          <label>السلفة<select name="advanceId" required defaultValue=""><option value="" disabled>اختر سلفة لها رصيد</option>{availableAdvances.map(a => <option key={a.id} value={a.id}>سلفة {a.number} · {date.format(new Date(a.createdAtUtc))} · متاح {money(a.remaining)}{a.reference ? ` · ${a.reference}` : ""}</option>)}</select></label>
          <label>كشف الشهر<select name="month" required defaultValue=""><option value="" disabled>اختر كشفًا معتمدًا</option>{statements.map(s => <option key={s.month} value={s.month}>{s.kind === "Opening" ? `الافتتاح قبل ${formatDateAr(s.openingAsOf!)}` : formatDateAr(s.month)} · متبقٍ {money(s.remaining)}</option>)}</select></label>
          <label>مبلغ التخصيص<input name="amount" type="number" min="0.01" max="100000000" step="0.01" required /></label>
          <button className="primary-button" disabled={busy}>مراجعة التخصيص</button>
        </form> : <p>لا يوجد كشف معتمد له مبلغ متبقٍ.</p>}
      </section>}
      <section className="list-card statement-details"><h2>سجل التخصيص</h2>
        {!data.applications.length ? <p>لم تخصص سلفة إلى كشف بعد.</p> : data.applications.map(p => <article className="statement-payment" key={p.id}>
          <strong>{money(p.amount)}</strong><Link href={p.kind === "Opening" ? `/dashboard/payroll/${id}/opening` : `/dashboard/payroll/${id}/statement?month=${p.month}`}>{p.kind === "Opening" ? "الافتتاح الموثق" : `كشف ${formatDateAr(p.month)}`} — ليس صرفًا جديدًا</Link>
        </article>)}
      </section>
    </>}
    <ConfirmationDialog open={review !== null} title={review?.kind === "apply" ? "مراجعة تخصيص السلفة" : "مراجعة السلفة المصروفة"} message={review?.message ?? ""} confirmLabel="تأكيد الحفظ" busy={busy} onConfirm={() => {
      if (!review || running.current) return;
      const command: Command = review.kind === "record" ? { kind: review.kind, body: review.body } : { kind: review.kind, advanceId: review.advanceId, body: review.body };
      void send({ ...command, key: crypto.randomUUID() });
    }} onCancel={() => setReview(null)} />
  </>;
}
