"use client";

import { type FormEvent, useCallback, useEffect, useRef, useState } from "react";
import Link from "next/link";
import { ConfirmationDialog, ErrorState, LoadingState, PageHeader, useDashboardPermission } from "../components/dashboard-shell";
import { apiProblemMessage } from "../lib/api-problem";
import { formatDateAr, formatMoneyAr } from "../lib/formatters";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";

type Row = { employeeId: string; name: string; currency: string; baseDue: number; deductions: number; additions: number; privateCompensation: number; netDue: number; paidAmount: number; remainingAmount: number; isPaid: boolean; salaryBasis: string; coachMembershipId?: string };
type Data = {
  openingBalanceVerified?: boolean; month: string; timeZone?: string; recoveryScope: string; row: Row;
  statement: { id: string; createdAtUtc: string; note?: string } | null;
  quoteHash: string; canApprove: boolean; canPay: boolean; unallocatedAdvances?: number;
  payments: { id: string; sequence: number; amount: number; remainingAfter: number; reference?: string; createdAtUtc: string; kind?: string }[];
  sources: { adjustments: { Id: string; Kind: string; Amount: number; Reason?: string }[]; attendance: { Id: string; WorkDate: string; ApprovedDeduction: number; DeductionReason?: string | null }[]; privateSources: { Id: string; DeliveryId: string; Delta: number }[] };
};
type Action = "approve" | "disbursements";
type Command =
  | { kind: "approve"; body: { month: string; quoteHash: string; note: string } }
  | { kind: "disbursements"; body: { month: string; amount: number; expectedPaid: number; expectedRemaining: number; reference: string } };
type Review = Command & { message: string };
type Attempt = Command & { key: string; acceptedId?: string; retryAllowed?: boolean };
type Props = { id: string; month: string; embedded?: boolean };
const storageError = "تعذر حفظ محاولة الرواتب في هذا التبويب. أعد تحميل الكشف وراجع سجل التسوية.";

function readAttempt(slot: string, month: string): Attempt | null {
  const raw = sessionStorage.getItem(slot);
  if (raw === null) return null;
  const { key, kind, body } = JSON.parse(raw);
  if (typeof key !== "string" || !/^[a-f0-9-]{36}$/i.test(key) || body?.month !== month) throw new Error();
  if (kind === "approve" && typeof body.quoteHash === "string" && typeof body.note === "string" && body.note.length <= 500) {
    return { key, kind, body: { month, quoteHash: body.quoteHash, note: body.note } };
  }
  if (kind === "disbursements" && [body.amount, body.expectedPaid, body.expectedRemaining].every(Number.isFinite) &&
    body.amount > 0 && body.amount <= body.expectedRemaining && typeof body.reference === "string" && body.reference.length <= 120) {
    return { key, kind, body: { month, amount: body.amount, expectedPaid: body.expectedPaid, expectedRemaining: body.expectedRemaining, reference: body.reference } };
  }
  throw new Error();
}

function isDenied(error: unknown) {
  return error instanceof DashboardRequestError && [401, 403, 404].includes(error.status);
}

async function readResult(response: Response, fallback: string) {
  if (!response.ok) throw new DashboardRequestError(await apiProblemMessage(response, fallback), response.status);
  return response.json();
}

function responseId(value: unknown) {
  return value && typeof value === "object" && "id" in value && typeof value.id === "string" && value.id ? value.id : null;
}

export function PayrollStatement(props: Props) {
  return <StatementScreen key={`${props.id}:${props.month}`} {...props} />;
}

function StatementScreen({ id, month, embedded = false }: Props) {
  const path = `/api/v1/manage/payroll/${id}/statement`;
  const canReadAttendanceSource = useDashboardPermission("attendance.manage");
  const slotFor = useCallback((scope: string) => `academy-payroll-attempt:${scope}:${id}:${month}`, [id, month]);
  const [data, setData] = useState<Data | null>(null);
  const [verifiedAttendanceStatement, setVerifiedAttendanceStatement] = useState<Data | null>(null);
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const [reloadRequired, setReloadRequired] = useState(false);
  const [review, setReview] = useState<Review | null>(null);
  const [attempt, setAttempt] = useState<Attempt | null>(null);
  const running = useRef(false);
  const attendanceMember = data?.row.coachMembershipId;
  const attendanceDate = data?.sources.attendance[0]?.WorkDate;
  const attendanceSource = attendanceMember && attendanceDate && canReadAttendanceSource
    ? `/api/v1/manage/staff-attendance/${attendanceMember}?${new URLSearchParams({ date: attendanceDate })}`
    : null;
  useEffect(() => {
    if (!attendanceSource) return;
    const controller = new AbortController();
    void fetch(attendanceSource, { cache: "no-store", signal: controller.signal }).then(response => {
      if (!controller.signal.aborted) setVerifiedAttendanceStatement(response.ok ? data : null);
    }).catch(() => {
      if (!controller.signal.aborted) setVerifiedAttendanceStatement(null);
    });
    return () => controller.abort();
  }, [attendanceSource, data]);
  const read = useCallback(async (signal?: AbortSignal, scope?: string) => {
    const response = await fetch(`${path}?${new URLSearchParams({ month })}`, { cache: "no-store", signal, ...(scope ? { headers: { "X-Payroll-Scope": scope } } : {}) });
    const value = await readResult(response, [401, 403, 404].includes(response.status) ? "هذا الكشف غير متاح لهذا الحساب." : "تعذر تحميل كشف المستحقات.") as Data;
    if (scope && value.recoveryScope !== scope) throw new DashboardRequestError("تغيّر الحساب الحالي.", 403);
    if (value.month !== month || value.row?.employeeId !== id || !Array.isArray(value.payments) ||
      typeof value.recoveryScope !== "string" || !/^[a-f0-9]{64}$/i.test(value.recoveryScope)) {
      throw new Error();
    }
    return value;
  }, [path, id, month]);

  const open = useCallback((value: Data) => {
    setData(value);
    try {
      setAttempt(readAttempt(slotFor(value.recoveryScope), month));
      setReloadRequired(false);
      setError("");
    } catch {
      setAttempt(null);
      setReloadRequired(true);
      setError(storageError);
    }
  }, [slotFor, month]);

  useEffect(() => {
    const controller = new AbortController();
    void read(controller.signal).then(value => {
      if (!controller.signal.aborted) open(value);
    }).catch(error => {
      if (!controller.signal.aborted) setError(error instanceof DashboardRequestError ? error.message : "تعذر تحميل كشف المستحقات.");
    });
    return () => controller.abort();
  }, [read, open]);

  async function load(scope: string) {
    const value = await read(undefined, scope);
    setData(value);
    setReloadRequired(false);
    return value;
  }

  function readFailed(cause: unknown, fallback: string) {
    setReloadRequired(true);
    if (isDenied(cause)) {
      setData(null);
      setAttempt(null);
      setNotice("");
      setError("هذا الكشف غير متاح لهذا الحساب.");
    } else {
      setError(cause instanceof DashboardRequestError && cause.status < 500 ? cause.message : fallback);
    }
  }

  async function run(work: () => Promise<void>) {
    if (running.current) return;
    running.current = true;
    setBusy(true);
    setError("");
    try { await work(); }
    finally { running.current = false; setBusy(false); }
  }

  function prepare(event: FormEvent<HTMLFormElement>, kind: Action) {
    event.preventDefault();
    if (!data || running.current || attempt || reloadRequired) return;
    const fields = new FormData(event.currentTarget);
    setError("");
    setNotice("");
    if (kind === "approve") {
      setReview({ kind, body: { month, quoteHash: data.quoteHash, note: String(fields.get("note") ?? "") }, message: `${data.row.name} · ${monthName} · ${formatMoneyAr(data.row.netDue, data.row.currency)}. الاعتماد يثبت بنود الكشف. الاعتماد ليس صرفًا.` });
      return;
    }
    const entered = String(fields.get("amount") ?? "");
    const amount = Number(entered);
    if (!/^(?:\d+(?:\.\d{1,2})?|\.\d{1,2})$/.test(entered) || amount <= 0 || amount > Math.min(data.row.remainingAmount, 100000000)) {
      setError(`أدخل مبلغًا موجبًا بخانتين عشريتين على الأكثر، في حدود المتبقي ${formatMoneyAr(data.row.remainingAmount, data.row.currency)}.`);
      return;
    }
    const remaining = (Math.round(data.row.remainingAmount * 100) - Math.round(amount * 100)) / 100;
    setReview({
      kind,
      body: { month, amount, expectedPaid: data.row.paidAmount, expectedRemaining: data.row.remainingAmount, reference: String(fields.get("reference") ?? "") },
      message: `${data.row.name} · ${monthName}. الصرف ${formatMoneyAr(amount, data.row.currency)} والمتبقي ${formatMoneyAr(remaining, data.row.currency)}. أكد أن الدفع حدث فعلاً. لا ينفذ التطبيق تحويلًا بنكيًا.`,
    });
  }

  function clearAttempt(command: Attempt, scope: string) {
    const slot = slotFor(scope);
    if (readAttempt(slot, month)?.key === command.key) sessionStorage.removeItem(slot);
    setAttempt(null);
  }

  async function showSaved(command: Attempt, scope: string, acceptedId: string) {
    setAttempt({ ...command, acceptedId });
    try {
      const current = await load(scope);
      const found = command.kind === "approve" ? current.statement?.id === acceptedId : current.payments.some(payment => payment.id === acceptedId);
      if (!found) throw new Error();
      clearAttempt(command, scope);
      setNotice(command.kind === "approve" ? "تم اعتماد الكشف دون تسجيل صرف." : "تم تسجيل الصرف وتحديث المتبقي.");
    } catch (cause) {
      readFailed(cause, "حُفظ الطلب، وتعذر تحديث الكشف. أعد قراءة الكشف قبل بدء عملية جديدة.");
    }
  }

  async function send(command: Attempt) {
    if (!data) return;
    const scope = data.recoveryScope;
    await run(async () => {
      setNotice("");
      try {
        const slot = slotFor(scope);
        const saved = readAttempt(slot, month);
        if (saved && saved.key !== command.key) {
          setAttempt(saved);
          setError("تحقّق من المحاولة السابقة قبل تسجيل عملية جديدة.");
          return;
        }
        if (!saved) {
          const { kind, body, key } = command;
          sessionStorage.setItem(slot, JSON.stringify({ kind, body, key }));
        }
      } catch {
        setReloadRequired(true);
        setError(storageError);
        return;
      }
      setAttempt({ ...command, retryAllowed: false });
      try {
        const response = await csrfRequest(`${path}/${command.kind}`, "POST", command.body, { "Idempotency-Key": command.key, "X-Payroll-Scope": scope }, { announce: false });
        const acceptedId = responseId(await response.json());
        if (!acceptedId) throw new Error();
        await showSaved(command, scope, acceptedId);
      } catch (cause) {
        if (isDenied(cause)) {
          readFailed(cause, "");
        } else if (cause instanceof DashboardRequestError && [400, 409, 422].includes(cause.status)) {
          try { clearAttempt(command, scope); }
          catch { setReloadRequired(true); setError(storageError); return; }
          setError(cause.message);
          if (cause.status === 409) {
            try { await load(scope); }
            catch (readError) { readFailed(readError, "تغير الكشف. أعد تحميله قبل المحاولة."); }
          }
        } else {
          setError("تعذر تأكيد الحفظ. تحقّق من الطلب قبل بدء عملية جديدة.");
        }
      }
    });
  }

  async function verify() {
    await run(async () => {
      if (!attempt || !data) {
        try { open(await read()); }
        catch (cause) { readFailed(cause, "تعذر تحميل كشف المستحقات."); }
        return;
      }
      const scope = data.recoveryScope;
      if (attempt.acceptedId) { await showSaved(attempt, scope, attempt.acceptedId); return; }
      const query = new URLSearchParams({ month, action: attempt.kind, requestKey: attempt.key });
      try {
        const response = await fetch(`${path}/by-request?${query}`, { cache: "no-store", headers: { "X-Payroll-Scope": scope } });
        if (response.status === 404) {
          setAttempt({ ...attempt, retryAllowed: true });
          setError("لا توجد نتيجة مؤكدة لهذا الطلب بعد. يمكنك إعادة إرسال الطلب نفسه.");
          return;
        }
        const acceptedId = responseId(await readResult(response, "تعذر التحقق من الطلب."));
        if (!acceptedId) throw new Error();
        await showSaved(attempt, scope, acceptedId);
      } catch (cause) { readFailed(cause, "تعذر التحقق من الطلب. أعد المحاولة."); }
    });
  }

  const selectedMonth = new Date(`${month}T00:00:00Z`);
  const monthName = Number.isFinite(selectedMonth.valueOf()) ? new Intl.DateTimeFormat("ar-EG-u-nu-latn", { month: "long", year: "numeric", timeZone: "UTC" }).format(selectedMonth) : "الشهر المحدد";
  const money = (amount: number) => formatMoneyAr(amount, data?.row.currency);
  const canAct = !attempt && !reloadRequired;
  const payrollLink = `/dashboard/payroll?employeeId=${encodeURIComponent(id)}&month=${month.slice(0, 7)}`;
  return <>
    {!embedded && <PageHeader title="كشف المستحقات والصرف" context="الرواتب" secondaryActions={[{ label: "عودة للرواتب", href: payrollLink }]} />}
    {error && <ErrorState message={error} />}
    {notice && <p role="status" className="success-note">{notice}</p>}
    {!data && !error && <LoadingState />}
    {attempt && data ? <section className="detail-card" aria-labelledby="payroll-attempt-title" aria-busy={busy}>
      <h2 id="payroll-attempt-title">{attempt.acceptedId ? "تحديث الكشف بعد الحفظ" : attempt.kind === "approve" ? "محاولة اعتماد بانتظار التحقق" : "محاولة صرف بانتظار التحقق"}</h2>
      {!error && <p>تحقّق من نتيجة المحاولة قبل بدء عملية جديدة.</p>}
      <div className="form-actions">
        <button className="primary-button" disabled={busy} onClick={() => void verify()}>{attempt.acceptedId ? "إعادة قراءة الكشف" : attempt.kind === "approve" ? "التحقق من اعتماد الكشف" : "التحقق من تسجيل الصرف"}</button>
        {!attempt.acceptedId && attempt.retryAllowed && <button className="secondary-button" disabled={busy} onClick={() => void send(attempt)}>إعادة إرسال الطلب نفسه</button>}
      </div>
    </section> : error && (reloadRequired || !data) && <div className="form-actions"><button className="secondary-button" disabled={busy} onClick={() => void verify()}>إعادة تحميل الكشف</button></div>}
    {data && <>
      <p><strong>{data.row.name}</strong> · {monthName} · {data.statement ? "كشف معتمد" : data.row.isPaid ? "مسدد بالمسار السابق" : "معاينة للمراجعة"}</p>
      <section className="statement-metrics payroll-statement-metrics" aria-label="مبالغ الكشف">
        {([["الاستحقاق", data.row.netDue], ["المسدد", data.row.paidAmount], ["المتبقي", data.row.remainingAmount]] as const).map(([label, value]) => <div key={label}><small>{label}</small><strong><bdi>{money(value)}</bdi></strong></div>)}
      </section>
      {(data.unallocatedAdvances ?? 0) > 0 && <p role="note">سلف غير مخصصة بقيمة {money(data.unallocatedAdvances!)}. تُحسب السلفة ضمن المسدد بعد تخصيصها. راجع <Link href={`/dashboard/payroll/${id}/advances`}>السلف قبل صرف مبلغ جديد</Link>.</p>}
      <section className="detail-card statement-details">
        <h2>تفصيل الاستحقاق</h2>
        <div className="statement-lines"><Link href={`/dashboard/staff/employees/${id}/salary`}>الأساسي · {money(data.row.baseDue)}</Link><span>الخصومات · {money(data.row.deductions)}</span><span>الإضافات · {money(data.row.additions)}</span></div>
        {data.row.salaryBasis === "LegacyUnverified" && <p role="alert">مرجع الأساسي القديم غير مؤرخ. راجع سجل الراتب قبل الاعتماد.</p>}
        {data.row.coachMembershipId && data.row.privateCompensation !== 0 && <Link href={`/dashboard/academy/coaches/${data.row.coachMembershipId}?tab=accounts`}>صافي الخاص ضمن الاستحقاق · {money(data.row.privateCompensation)}</Link>}
        {data.sources.adjustments.map(item => <p key={item.Id}>{item.Kind === "Addition" ? "إضافة" : "خصم"} · {money(item.Amount)} · {item.Reason ?? "سند مسجل"}</p>)}
        {data.sources.attendance.map(item => {
          const label = `خصم دوام ${formatDateAr(item.WorkDate)}`;
          const source = data.row.coachMembershipId && `/dashboard/attendance/staff/${data.row.coachMembershipId}?${new URLSearchParams({ date: item.WorkDate })}`;
          return <p key={item.Id}>
            {source && attendanceSource && verifiedAttendanceStatement === data ? <Link href={source}>{label}</Link> : label}
            {" · "}<bdi>{money(item.ApprovedDeduction)}</bdi>
            {item.DeductionReason?.trim() && <><br /><span>{item.DeductionReason.trim()}</span></>}
          </p>;
        })}
        {data.sources.privateSources.length > 0 && <p>{data.sources.privateSources.length} حركة خاص مرتبطة بمصادرها في ملف المدرب</p>}
      </section>
      {data.canApprove && canAct && <section className="detail-card"><h2>اعتماد الكشف</h2><form className="form-shell" aria-label="اعتماد الكشف" onSubmit={event => prepare(event, "approve")}><label>ملاحظة (اختياري)<textarea name="note" maxLength={500} /></label><div className="form-actions"><button className="primary-button" disabled={busy}>مراجعة اعتماد الكشف</button></div></form></section>}
      {data.canPay && canAct && <section className="detail-card"><h2>تسجيل صرف</h2><form className="form-shell" aria-label="تسجيل صرف" noValidate onSubmit={event => prepare(event, "disbursements")} key={data.row.paidAmount}><label>مبلغ الصرف<input name="amount" type="number" min="0.01" max={data.row.remainingAmount} step="0.01" required /></label><label>مرجع الصرف (اختياري)<input name="reference" maxLength={120} /></label><div className="form-actions"><button className="primary-button" disabled={busy}>مراجعة الصرف</button></div></form></section>}
      <section className="list-card"><h2>سجل تسوية المستحق</h2>
        {data.payments.length === 0 ? <p>{data.row.isPaid && !data.statement ? "تم السداد بالمسار السابق." : "لا توجد تسويات مسجلة على هذا الكشف."}</p> : data.payments.map(payment => <article key={payment.id} className="statement-payment">
          <strong>{money(payment.amount)}</strong>
          <span>{payment.kind === "AdvanceApplication" ? "تخصيص سلفة سابقة — ليس صرفًا جديدًا" : "صرف مباشر"}{payment.createdAtUtc && ` · ${new Intl.DateTimeFormat("ar-EG-u-nu-latn", { dateStyle: "long", timeZone: data.timeZone ?? "Africa/Cairo" }).format(new Date(payment.createdAtUtc))}`}</span>
          <small>المتبقي بعد الحركة {money(payment.remainingAfter)}</small>
          {payment.reference && <small>{payment.reference}</small>}
        </article>)}
      </section>
      <details className="detail-card"><summary>نطاق الكشف وحساب الموظف</summary><p>الأرقام تخص الشهر المحدد. السلفة المخصصة تُحسب ضمن المسدد. {data.openingBalanceVerified ? "الافتتاح الموثق ظاهر في حساب الموظف." : "الافتتاح والسلف التاريخية غير المستوردة غير موثقة."}</p><Link href={`/dashboard/payroll/${id}/account`}>حساب الموظف والأشهر السابقة</Link></details>
    </>}
    <ConfirmationDialog open={review !== null} title={review?.kind === "approve" ? "اعتماد كشف المستحقات" : "تأكيد تسجيل الصرف"} message={review?.message ?? ""} confirmLabel={review?.kind === "approve" ? "تأكيد اعتماد الكشف" : "تأكيد تسجيل الصرف"} busy={busy} onConfirm={() => { if (review && !running.current) { setReview(null); void send({ ...review, key: crypto.randomUUID() }); } }} onCancel={() => setReview(null)} />
  </>;
}
