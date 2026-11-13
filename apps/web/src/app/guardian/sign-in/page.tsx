"use client";

import Link from "next/link";
import { useEffect, useRef, useState, type FormEvent } from "react";
import styles from "../../../components/login-form.module.css";
import { canonicalDigits } from "../../../lib/number-input";
import { webAuthRequest } from "../../../lib/web-auth-request";

const auth = "/api/v1/auth";
type Availability = "loading" | "ready" | "unavailable" | "error";

function egyptianMobile(value: string) {
  const match = canonicalDigits(value).replace(/[\s()-]/g, "").match(/^(?:\+20|0020|20)?0?(1[0125][0-9]{8})$/);
  return match ? `+20${match[1]}` : null;
}

export default function GuardianSignInPage() {
  const [availability, setAvailability] = useState<Availability>("loading");
  const [attempt, setAttempt] = useState(0);
  const [phone, setPhone] = useState("");
  const [code, setCode] = useState("");
  const [challengeId, setChallengeId] = useState("");
  const [busy, setBusy] = useState(false);
  const [conflict, setConflict] = useState(false);
  const [fieldError, setFieldError] = useState("");
  const [error, setError] = useState("");
  const field = useRef<HTMLInputElement>(null);
  const title = useRef<HTMLHeadingElement>(null);
  const codeStep = Boolean(challengeId);

  useEffect(() => {
    const controller = new AbortController();
    fetch(`${auth}/guardian/otp/availability`, { cache: "no-store", signal: controller.signal }).then(async response => {
      if (!response.ok) throw new Error();
      const result = await response.json();
      if (typeof result.enabled !== "boolean") throw new Error();
      setAvailability(result.enabled ? "ready" : "unavailable");
    }).catch(() => {
      if (!controller.signal.aborted) setAvailability("error");
    });
    return () => controller.abort();
  }, [attempt]);

  useEffect(() => {
    if (conflict) title.current?.focus();
    else if (availability === "ready") field.current?.focus();
  }, [availability, challengeId, conflict]);

  function acceptResponse(response: Response) {
    if (response.status === 409) {
      setConflict(true);
      return false;
    }
    if (response.status === 501) {
      setAvailability("unavailable");
      return false;
    }
    return true;
  }

  async function requestCode() {
    if (busy) return;
    const phoneNumber = egyptianMobile(phone);
    if (!phoneNumber) {
      setFieldError(phone.trim() ? "اكتب رقم موبايل مصري صحيح." : "اكتب رقم الهاتف.");
      field.current?.focus();
      return;
    }
    setBusy(true);
    setFieldError("");
    setError("");
    try {
      const response = await webAuthRequest(auth, "guardian/otp/request", { phoneNumber });
      if (!acceptResponse(response)) return;
      if (!response.ok) throw new Error("تعذر طلب رمز التحقق. حاول مرة أخرى.");
      const result = await response.json().catch(() => null);
      if (typeof result?.challengeId !== "string" || !result.challengeId)
        throw new Error("تعذر بدء التحقق. اطلب رمزًا جديدًا.");
      setCode("");
      setChallengeId(result.challengeId);
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "تعذر طلب رمز التحقق.");
    } finally {
      setBusy(false);
    }
  }

  async function verifyCode() {
    if (busy) return;
    if (!/^[0-9]{6}$/.test(code)) {
      setFieldError("اكتب رمز التحقق من ٦ أرقام.");
      field.current?.focus();
      return;
    }
    setBusy(true);
    setFieldError("");
    setError("");
    try {
      const response = await webAuthRequest(auth, "guardian/otp/verify", {
        phoneNumber: egyptianMobile(phone), challengeId, code,
      });
      if (!acceptResponse(response)) return;
      if (response.status === 401) throw new Error("رمز التحقق غير صحيح أو انتهت صلاحيته. يمكنك طلب رمز جديد.");
      if (!response.ok) throw new Error("تعذر التحقق الآن. حاول مرة أخرى.");
      const result = await response.json().catch(() => null);
      if (result?.authenticated !== true) throw new Error("تعذر تأكيد الدخول. حاول مرة أخرى.");
      window.location.replace("/guardian");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "تعذر التحقق.");
      setBusy(false);
    }
  }

  async function logout() {
    if (busy) return;
    setBusy(true);
    setError("");
    try {
      const response = await webAuthRequest(auth, "logout");
      if (!response.ok && response.status !== 401) throw new Error("تعذر تسجيل الخروج. حاول مرة أخرى.");
      window.location.reload();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "تعذر تسجيل الخروج.");
      setBusy(false);
    }
  }

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void (codeStep ? verifyCode() : requestCode());
  }

  return <main className="center-shell">
    <section className={`login-card ${styles.card}`} aria-labelledby="guardian-sign-in-title">
      <h1 id="guardian-sign-in-title" ref={title} tabIndex={-1}>{conflict ? "حساب مفتوح بالفعل" : "دخول ولي الأمر"}</h1>
      {conflict ? <div className={styles.recovery}>
        <p>لتبديل الحساب، سجّل الخروج أولًا.</p>
        {error && <p className="error-message" role="alert">{error}</p>}
        <button className="secondary-button" type="button" disabled={busy} onClick={() => void logout()}>
          {busy ? "جارٍ تسجيل الخروج…" : "تسجيل الخروج لتبديل الحساب"}
        </button>
        {busy
          ? <span className={styles.link} aria-disabled="true">متابعة بالحساب الحالي</span>
          : <Link className={styles.link} href="/">متابعة بالحساب الحالي</Link>}
      </div> : <>
        {availability === "loading" && <p role="status">جارٍ التحقق من إتاحة الدخول…</p>}
        {availability === "error" && <div className={styles.recovery}>
          <p className="error-message" role="alert">تعذر تحميل طريقة الدخول.</p>
          <button type="button" className="secondary-button" onClick={() => {
            setAvailability("loading");
            setAttempt(value => value + 1);
          }}>إعادة المحاولة</button>
        </div>}
        {availability === "unavailable" && <p>استخدم تطبيق الأكاديمية لتسجيل الدخول برقم الهاتف.</p>}
        {availability === "ready" && <form className={styles.form} method="post" noValidate onSubmit={submit}>
          {codeStep && <p>رقم الهاتف <bdi>{egyptianMobile(phone)}</bdi></p>}
          <div className="field">
            <div className={styles.fieldTitle}>
              <label htmlFor="guardian-login-field">{codeStep ? "رمز التحقق" : "رقم الهاتف"}</label>
              {!codeStep && <span id="guardian-phone-country">مصر <bdi dir="ltr">+20</bdi></span>}
            </div>
            <input key={codeStep ? "code" : "phone"} ref={field} id="guardian-login-field"
              type={codeStep ? "text" : "tel"} dir="ltr" inputMode={codeStep ? "numeric" : "tel"}
              autoComplete={codeStep ? "one-time-code" : "tel"} required readOnly={busy}
              aria-invalid={Boolean(fieldError) || undefined}
              aria-describedby={[!codeStep && "guardian-phone-country", fieldError && "guardian-field-error"].filter(Boolean).join(" ") || undefined}
              value={codeStep ? code : phone} onChange={event => {
                if (codeStep) setCode(canonicalDigits(event.target.value));
                else setPhone(event.target.value);
                setFieldError("");
                setError("");
              }} />
            {fieldError && <span id="guardian-field-error" className="error-message" role="alert">{fieldError}</span>}
          </div>
          {error && <p className="error-message" role="alert">{error}</p>}
          <button className="primary-button" disabled={busy}>{busy ? "جارٍ التحقق…" : codeStep ? "تأكيد الدخول" : "طلب رمز التحقق"}</button>
          {codeStep && <div className={styles.codeActions}>
            <button type="button" className="secondary-button" disabled={busy} onClick={() => {
              setChallengeId("");
              setCode("");
              setError("");
              setFieldError("");
            }}>تعديل الرقم</button>
            <button type="button" className="secondary-button" disabled={busy} onClick={() => void requestCode()}>طلب رمز جديد</button>
          </div>}
        </form>}
        <Link className={styles.link} href="/login">دخول المالك والإداري</Link>
      </>}
    </section>
  </main>;
}
