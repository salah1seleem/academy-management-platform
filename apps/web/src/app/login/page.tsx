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
      if (!response.ok) throw new Error("بيانات الدخول غير صحيحة.");
      router.replace("/dashboard");
    } catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر تسجيل الدخول."); setBusy(false); }
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
    </section>
  </main>;
}
