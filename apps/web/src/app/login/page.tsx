"use client";

import { FormEvent, useState } from "react";
import { useRouter } from "next/navigation";

async function csrf() {
  const response = await fetch("/api/v1/auth/csrf");
  if (!response.ok) throw new Error("تعذر بدء الطلب الآمن.");
  return (await response.json()) as { token: string };
}

export default function LoginPage() {
  const router = useRouter();
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);

  async function dashboardLogin(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy(true); setError("");
    const data = new FormData(event.currentTarget);
    try {
      const { token } = await csrf();
      const response = await fetch("/api/v1/auth/login", {
        method: "POST", headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
        body: JSON.stringify({ email: data.get("email"), password: data.get("password") }),
      });
      if (response.status === 409) throw new Error("توجد جلسة دخول نشطة. سجّل الخروج أولًا قبل الدخول بحساب آخر.");
      if (!response.ok) throw new Error("بيانات الدخول غير صحيحة.");
      const result = await response.json();
      router.replace(result.requiresContextSelection ? "/dashboard/context" : "/dashboard");
    } catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر تسجيل الدخول."); setBusy(false); }
  }

  async function logout() {
    setBusy(true); setError("");
    try {
      const { token } = await csrf();
      const response = await fetch("/api/v1/auth/logout", { method: "POST", headers: { "X-CSRF-TOKEN": token } });
      if (!response.ok && response.status !== 401) throw new Error("تعذر تسجيل الخروج. حاول مرة أخرى.");
      // Full navigation also discards role/tenant-scoped client state and CSRF.
      window.location.reload();
    } catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر تسجيل الخروج."); setBusy(false); }
  }

  return <main className="center-shell">
    <section className="login-card" aria-labelledby="login-title">
      <span className="eyebrow">منصة إدارة الأكاديمية</span><h1 id="login-title">تسجيل الدخول</h1>
      <form onSubmit={(event) => void dashboardLogin(event)}>
        <label className="field">البريد الإلكتروني<input name="email" type="email" autoComplete="username" required /></label>
        <label className="field">كلمة المرور<input name="password" type="password" autoComplete="current-password" required /></label>
        <button className="primary-button" disabled={busy}>{busy ? "جارٍ الدخول…" : "دخول آمن"}</button>
      </form>
      {error && <p className="error-message" role="alert">{error}</p>}
      {error.startsWith("توجد جلسة دخول نشطة") && <button className="secondary-button" disabled={busy} onClick={() => void logout()}>تسجيل خروج الحساب الحالي</button>}
    </section>
  </main>;
}
