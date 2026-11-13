"use client";

import { useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { LoginForm, type LoginCredentials } from "../../../components/login-form";
import styles from "../../../components/login-form.module.css";
import { webAuthRequest } from "../../../lib/web-auth-request";

export default function PlatformLogin() {
  const router = useRouter();
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  async function submit(credentials: LoginCredentials) {
    setBusy(true);
    setError("");
    try {
      const response = await webAuthRequest("/api/v1/platform/auth", "login", credentials);
      if (response.status === 401 || response.status === 403) {
        throw new Error("تحقّق من البريد الإلكتروني وكلمة المرور ورمز التحقق.");
      }
      if (!response.ok) throw new Error("تعذر تسجيل الدخول الآن. حاول مرة أخرى.");
      const result = await response.json().catch(() => null);
      if (result?.authenticated !== true) throw new Error("تعذر تأكيد الدخول. حدّث الصفحة وأعد المحاولة.");
      router.replace("/platform");
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "تعذر تسجيل الدخول.");
      setBusy(false);
    }
  }

  return <main className="center-shell">
    <section className={`login-card ${styles.card}`} aria-labelledby="platform-title">
      <header className={styles.header}>
        <h1 id="platform-title">دخول إدارة المنصة</h1>
        <p className="muted">استخدم حساب مشغّل المنصة ورمز التحقق.</p>
      </header>
      <LoginForm busy={busy} error={error} platform onLogin={submit} />
      <Link className={styles.link} href="/login">دخول الأكاديمية</Link>
    </section>
  </main>;
}
