"use client";

import Link from "next/link";
import { useCallback, useEffect, useRef, useState } from "react";
import { ContextHelp, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { browserPasskey } from "./staff-passkeys";

const root = "/api/v1/staff/passkeys/attendance";
const pendingMessage = "تعذر تأكيد التسجيل. تحقّق من النتيجة قبل بدء عملية أخرى.";
const storageKey = (membershipId: string) => `academy-staff-clock:${membershipId}`;
function deadline(signal?: AbortSignal) {
  const timeout = AbortSignal.timeout(12000);
  return signal ? AbortSignal.any([signal, timeout]) : timeout;
}
type Operation = "CheckIn" | "CheckOut";
type Receipt = { attendanceRecorded: boolean; attendanceId: string; workPeriodId: string; operation: Operation; workDate: string; confirmedAtUtc: string; trustSource: string };
type State = {
  membershipId?: string;
  configured: boolean;
  profileMissing?: boolean;
  hasCredential: boolean;
  hasOpenAttendance: boolean;
  requiresSourceReview: boolean;
  branches: { id: string; name: string; configured: boolean; enabled?: boolean; radiusMeters: number; maxAccuracyMeters: number; maxAgeSeconds: number; challengeSeconds: number; attendanceRequired: boolean }[];
  recent: Receipt[];
};
type Proof = { challengeId: string; intent: { branchId: string; operation: Operation; latitude: number; longitude: number; accuracyMeters: number; capturedAtUtc: string }; credential: unknown };
type Attempt = { challengeId: string; operation: Operation; membershipId?: string; proof?: Proof };
const enabled = (branch: State["branches"][number]) => branch.configured && branch.enabled !== false;

function readAttempt(membershipId?: string): Attempt | undefined {
  if (!membershipId) return;
  try {
    const value = JSON.parse(sessionStorage.getItem(storageKey(membershipId)) ?? "null");
    if (/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value?.challengeId) &&
        (value.operation === "CheckIn" || value.operation === "CheckOut"))
      return { membershipId, challengeId: value.challengeId, operation: value.operation };
  } catch { /* Recovery while the page is open still uses memory. */ }
}

function rememberAttempt({ membershipId, challengeId, operation }: Attempt) {
  if (!membershipId) return;
  try { sessionStorage.setItem(storageKey(membershipId), JSON.stringify({ challengeId, operation })); }
  catch { /* The signed proof is held in memory for an in-page retry. */ }
}

function forgetAttempt({ membershipId, challengeId }: Attempt) {
  if (!membershipId || readAttempt(membershipId)?.challengeId !== challengeId) return;
  try { sessionStorage.removeItem(storageKey(membershipId)); }
  catch { /* The server retains the confirmed result. */ }
}

export function StaffVerifiedAttendance() {
  const [state, setState] = useState<State | null>(null);
  const [branch, setBranch] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [message, setMessage] = useState("");
  const [waiting, setWaiting] = useState(false);
  const [pending, setPending] = useState<Attempt>();
  const working = useRef(false);
  const mounted = useRef(false);

  const load = useCallback(async (signal?: AbortSignal) => {
    const response = await fetch(root, { cache: "no-store", signal: deadline(signal) });
    if (!response.ok) throw Error();
    const next = await response.json() as State;
    if (typeof next?.configured !== "boolean" || (next.configured && !Array.isArray(next.branches))) throw Error();
    if (mounted.current && !signal?.aborted) {
      setState(next);
      setBranch(current => next.branches?.some(b => b.id === current && enabled(b)) ? current : "");
    }
    return next;
  }, []);

  const confirm = useCallback((result: Receipt, attempt: Attempt) => {
    if (result.attendanceRecorded !== true || !result.attendanceId || !result.workPeriodId ||
        result.operation !== attempt.operation || result.trustSource !== "ServerVerified" ||
        !Number.isFinite(Date.parse(result.confirmedAtUtc))) throw Error();
    if (!mounted.current) return;
    forgetAttempt(attempt);
    setPending(undefined);
    setMessage(result.operation === "CheckIn" ? "تم تسجيل حضورك بوقت الخادم المؤكد." : "تم تسجيل انصرافك للفترة نفسها بوقت الخادم المؤكد.");
  }, []);

  const loadConfirmed = useCallback(async (signal?: AbortSignal) => {
    try { await load(signal); }
    catch {
      if (!mounted.current || signal?.aborted) return;
      setState(null);
      setError("العملية مؤكدة. تعذر تحديث الحالة؛ أعد القراءة قبل التسجيل.");
    }
  }, [load]);

  const recover = useCallback(async (attempt: Attempt, signal?: AbortSignal) => {
    try {
      const suffix = attempt.proof ? "" : "?includeState=true";
      const response = await fetch(`${root}/attempts/${encodeURIComponent(attempt.challengeId)}${suffix}`, { cache: "no-store", signal: deadline(signal) });
      if (!mounted.current || signal?.aborted) return;
      if (response.status === 401 || response.status === 403) {
        setState(null);
        setPending(undefined);
        setError("تعذر التحقق من نتيجة التسجيل في الجلسة الحالية.");
        return;
      }
      if (response.status === 404 && attempt.proof) {
        setError("لم تُحفظ نتيجة مؤكدة لهذه المحاولة بعد. يمكنك إعادة إرسال الطلب نفسه.");
        return;
      }
      if (!response.ok && response.status !== 404) throw Error();
      const result: (Receipt & { attemptState?: string }) | null = response.ok ? await response.json() : null;
      if (!mounted.current || signal?.aborted) return;
      if (result?.attendanceRecorded === true) {
        confirm(result, attempt);
        await loadConfirmed(signal);
        return;
      }
      if (result?.attendanceRecorded === false && result.attemptState === "Pending") {
        setWaiting(true);
        return;
      }
      if (!result || (result.attendanceRecorded === false && result.attemptState === "Closed")) {
        setState(null);
        await load(signal);
        if (!mounted.current || signal?.aborted) return;
        forgetAttempt(attempt);
        setPending(undefined);
        setError(result ? "انتهت المحاولة دون تسجيل. اختر الفرع لبدء محاولة جديدة." : "لم تعد هذه المحاولة متاحة. حُدّثت حالة الدوام.");
        return;
      }
      throw Error();
    } catch {
      if (mounted.current && !signal?.aborted) setError("تعذر التحقق من نتيجة التسجيل. أعد المحاولة.");
    }
  }, [confirm, load, loadConfirmed]);

  const restore = useCallback(async (signal?: AbortSignal) => {
    const next = await load(signal);
    if (!mounted.current || signal?.aborted) return;
    const attempt = readAttempt(next.membershipId);
    if (!attempt) return;
    setPending(attempt);
    setBusy(true);
    await recover(attempt, signal);
  }, [load, recover]);

  useEffect(() => {
    const controller = new AbortController();
    mounted.current = true;
    working.current = true;
    void Promise.resolve().then(() => {
      if (!controller.signal.aborted) return restore(controller.signal);
    }).catch(() => {
      if (!controller.signal.aborted) setError("تعذر قراءة حالة حضورك. أعد المحاولة.");
    }).finally(() => {
      if (!controller.signal.aborted) {
        working.current = false;
        setBusy(false);
      }
    });
    return () => {
      mounted.current = false;
      controller.abort();
    };
  }, [restore]);

  async function submit(operation: Operation) {
    if (working.current || (pending ? !pending.proof : !state?.branches?.some(b => b.id === branch && enabled(b))
        || (operation === "CheckIn" && state.hasOpenAttendance)
        || (operation === "CheckOut" && (!state.hasOpenAttendance || state.requiresSourceReview)))) return;
    working.current = true;
    setBusy(true);
    setError("");
    setMessage("");
    let attempt = pending;
    try {
      if (!attempt) {
        if (!window.isSecureContext || !navigator.geolocation) throw Error();
        const position = await new Promise<GeolocationPosition>((resolve, reject) =>
          navigator.geolocation.getCurrentPosition(resolve, reject, { enableHighAccuracy: true, maximumAge: 0, timeout: 20000 }));
        if (!mounted.current) return;
        const intent = {
          branchId: branch, operation, latitude: position.coords.latitude, longitude: position.coords.longitude,
          accuracyMeters: position.coords.accuracy, capturedAtUtc: new Date(position.timestamp).toISOString(),
        };
        const options = await (await csrfRequest(root + "/options", "POST", intent, {}, { announce: false, signal: deadline() })).json();
        if (options.purpose !== operation || !options.challengeId || !mounted.current) throw Error();
        const credential = await browserPasskey("verify", options.options);
        if (!mounted.current) return;
        const proof = { challengeId: options.challengeId, intent, credential };
        attempt = { challengeId: proof.challengeId, operation, proof, membershipId: state?.membershipId };
        rememberAttempt(attempt);
        setPending(attempt);
      }
      const result = await (await csrfRequest(root + "/complete", "POST", attempt.proof, {}, { announce: false, signal: deadline() })).json() as Receipt;
      confirm(result, attempt);
      if (mounted.current) await loadConfirmed();
    } catch (caught) {
      if (!mounted.current) return;
      if (caught instanceof DashboardRequestError && caught.status >= 400 && caught.status < 500 && ![408, 429].includes(caught.status)) {
        if (attempt && ![401, 403].includes(caught.status)) forgetAttempt(attempt);
        setPending(undefined);
        setError(caught.message);
        await load().catch(() => setState(null));
      } else {
        setError(attempt ? pendingMessage : "تعذر التحقق من الموقع أو التوقيع. أعد المحاولة من داخل نطاق الفرع أو اطلب مراجعة من التطبيق.");
      }
    } finally {
      working.current = false;
      if (mounted.current) setBusy(false);
    }
  }

  async function refresh() {
    if (working.current) return;
    working.current = true;
    setBusy(true);
    setError("");
    setWaiting(false);
    try {
      if (pending) await recover(pending);
      else await restore();
    } catch {
      if (mounted.current) {
        setState(null);
        setError("تعذّر تحديث سجل الحضور. أعد المحاولة.");
      }
    } finally {
      working.current = false;
      if (mounted.current) setBusy(false);
    }
  }

  const ready = state?.configured && state.hasCredential && !state.profileMissing;
  const selected = state?.branches?.find(b => b.id === branch && enabled(b));
  return <>
    <PageHeader title="حضوري وانصرافي" context="حسابي / الحضور الوظيفي" action={pending ? undefined : { label: "وسائل التحقق", href: "/dashboard/account/verification" }} />
    <section className="detail-card staff-verified-attendance" aria-label="التسجيل الذاتي للدوام">
      {error && <ErrorState message={error} />}
      {message && <p role="status">{message}</p>}
      {!state && !error && !busy && <LoadingState />}
      {busy && <p role="status">جارٍ التحقق من حالة الدوام…</p>}
      {state?.profileMissing && <p>لا يوجد ملف موظف مرتبط بحسابك. تواصل مع الإدارة.</p>}
      {state && !state.configured && !state.profileMissing && <p>يلزم إعداد التحقق الآمن من المسؤول؛ مسار المراجعة الإداري متاح.</p>}
      {state?.configured && !state.hasCredential && <p>أضف وسيلة تحقق فعالة من <Link href="/dashboard/account/verification">وسائل التحقق الخاصة بي</Link> قبل التسجيل.</p>}
      {ready && (!pending || pending.proof) && <>
        <div className="form-shell"><label>فرع الحضور<select aria-label="فرع الحضور" value={branch} disabled={busy || !!pending} onChange={e => setBranch(e.target.value)}>
          <option value="">اختر الفرع</option>
          {state.branches.map(b => <option key={b.id} value={b.id} disabled={!enabled(b)}>{b.name}{b.enabled === false ? " — التسجيل الذاتي موقوف" : !b.configured ? " — يلزم إعداد الموقع" : ""}</option>)}
        </select></label></div>
        {!state.branches.some(enabled) && <p>يلزم تفعيل التسجيل وإعداد موقع الفرع. المراجعة الإدارية متاحة.</p>}
        {selected && <p aria-label="سياسة الفرع المختار">النطاق {selected.radiusMeters} مترًا · الدقة حتى {selected.maxAccuracyMeters} مترًا · قراءة خلال {selected.maxAgeSeconds} ثانية · محاولة التوقيع {selected.challengeSeconds} ثانية. {selected.attendanceRequired ? "تسجيل الحضور مطلوب لفئتك بهذا الفرع." : "التسجيل متاح لفئتك دون إلزام."}</p>}
        {state.requiresSourceReview && <p>اطلب مراجعة الفترة المفتوحة قبل تسجيل الانصراف.</p>}
      </>}
      {pending && !pending.proof && <div>
        <h3>{pending.operation === "CheckIn" ? "محاولة الحضور السابقة" : "محاولة الانصراف السابقة"}</h3>
        {waiting && <p role="status">لم يظهر تسجيل مؤكد بعد. تحقّق مرة أخرى بعد انتهاء المحاولة.</p>}
      </div>}
      <div className="form-actions">
        {pending ? <>
          <button className="primary-button" disabled={busy} onClick={() => void refresh()}>التحقق من نتيجة التسجيل</button>
          {pending.proof && <button className="secondary-button" disabled={busy} onClick={() => void submit(pending.operation)}>إعادة إرسال الطلب نفسه</button>}
        </> : <>
          {ready && <button className="primary-button" disabled={busy || !selected || (state.hasOpenAttendance && state.requiresSourceReview)} onClick={() => void submit(state.hasOpenAttendance ? "CheckOut" : "CheckIn")}>{state.hasOpenAttendance ? "تسجيل انصرافي" : "تسجيل حضوري"}</button>}
          {(state || error) && <button className="secondary-button" disabled={busy} onClick={() => void refresh()}>تحديث حالة الحضور</button>}
        </>}
      </div>
      <div className="workspace-help"><ContextHelp title="طريقة التسجيل والخصوصية">
        <p>يُطلب موقعك عند التسجيل ويُفحص وفق سياسة الفرع. قد تستخدم وسيلة التحقق بصمة الجهاز أو رمزه.</p>
        <p>يتطلب التسجيل اتصالًا بالخادم. إذا تعذر الموقع أو التوقيع، اطلب إثبات حضور للمراجعة من تطبيق الموظف.</p>
        <p>الدوام الوظيفي مستقل عن حضور اللاعبين والرواتب.</p>
      </ContextHelp></div>
      {!!state?.recent?.length && <section aria-label="عملياتي المؤكدة"><h3>آخر العمليات المؤكدة</h3><ul className="staff-passkey-list">
        {state.recent.map(r => <li key={`${r.workPeriodId}:${r.operation}`}><strong>{r.operation === "CheckIn" ? "حضور مسجل" : "انصراف مسجل"}</strong><span>{new Date(r.confirmedAtUtc).toLocaleString("ar-EG-u-nu-latn")}</span><small>يوم العمل {r.workDate} · توقيت الخادم</small></li>)}
      </ul></section>}
    </section>
  </>;
}
