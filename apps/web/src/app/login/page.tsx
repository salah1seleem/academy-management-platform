"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { LoginForm, type LoginCredentials } from "../../components/login-form";
import styles from "../../components/login-form.module.css";
import { operationReturnPath } from "../../lib/operation-return";
import { webAuthRequest } from "../../lib/web-auth-request";

const auth = "/api/v1/auth";

export default function LoginPage() {
  const router = useRouter();
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [conflict, setConflict] = useState(false);
  const recoveryTitle = useRef<HTMLHeadingElement>(null);

  useEffect(() => {
    if (conflict) recoveryTitle.current?.focus();
  }, [conflict]);

  async function dashboardLogin({ email, password }: LoginCredentials) {
    setBusy(true);
    setError("");
    try {
      const response = await webAuthRequest(auth, "login", { email, password });
      if (response.status === 409) {
        setConflict(true);
        setBusy(false);
        return;
      }
      if (response.status === 401) throw new Error("تحقّق من البريد الإلكتروني وكلمة المرور.");
      if (response.status === 403) throw new Error("لوحة الأكاديمية متاحة للمالك والإداري.");
      if (!response.ok) throw new Error("تعذر تسجيل الدخول الآن. حاول مرة أخرى.");
      const result = await response.json().catch(() => null);
      if (result?.authenticated !== true) throw new Error("تعذر تأكيد الدخول. حدّث الصفحة وأعد المحاولة.");
      const next = operationReturnPath(new URLSearchParams(window.location.search).get("returnTo"));
      if (result.requiresContextSelection) {
        router.replace(next ? `/dashboard/context?returnTo=${encodeURIComponent(next)}` : "/dashboard/context");
      } else {
        router.replace(next ?? "/dashboard");
      }
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "تعذر تسجيل الدخول.");
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
      // Full navigation also discards role/tenant-scoped client state and CSRF.
      window.location.reload();
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "تعذر تسجيل الخروج.");
      setBusy(false);
    }
  }

  return <main className="center-shell">
    <section className={`login-card ${styles.card}`} aria-labelledby="login-title">
      {conflict ? <>
        <h1 id="login-title" ref={recoveryTitle} tabIndex={-1}>حساب مفتوح بالفعل</h1>
        <div className={styles.recovery}>
          <p>لتبديل الحساب، سجّل الخروج أولًا.</p>
          {error && <p className="error-message" role="alert">{error}</p>}
          <button className="secondary-button" type="button" disabled={busy} onClick={() => void logout()}>
            {busy ? "جارٍ تسجيل الخروج…" : "تسجيل الخروج لتبديل الحساب"}
          </button>
          {busy
            ? <span className={styles.link} aria-disabled="true">متابعة بالحساب الحالي</span>
            : <Link className={styles.link} href="/">متابعة بالحساب الحالي</Link>}
        </div>
      </> : <>
        <h1 id="login-title">تسجيل دخول الأكاديمية</h1>
        <LoginForm busy={busy} error={error} onLogin={dashboardLogin} />
        <Link className={styles.link} href="/guardian/sign-in">دخول ولي الأمر</Link>
      </>}
    </section>
  </main>;
}
