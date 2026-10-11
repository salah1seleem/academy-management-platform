"use client";
import { useState, type FormEvent } from "react";
import { useRouter } from "next/navigation";
import { ShieldCheck } from "lucide-react";
import { platformRequest } from "../../../features/platform-api";

export default function PlatformLogin() {
  const router = useRouter(); const [busy, setBusy] = useState(false); const [error, setError] = useState("");
  async function submit(e: FormEvent<HTMLFormElement>) {
    e.preventDefault(); if (busy) return; const form = new FormData(e.currentTarget); setBusy(true); setError("");
    try { await platformRequest("/api/v1/platform/auth/login", "POST", { email: form.get("email"), password: form.get("password"), code: form.get("code") }); router.replace("/platform"); }
    catch (e) { setError(e instanceof Error ? e.message : "تعذر الدخول."); setBusy(false); }
  }
  return <main className="center-shell"><section className="login-card" aria-labelledby="platform-title"><span className="eyebrow"><ShieldCheck aria-hidden="true" /> أكاديميتي · إدارة المنصة</span><h1 id="platform-title">دخول مشغّل المنصة</h1><p className="muted">حساب مستقل محمي بالتحقق الثنائي، وليس حساب مالك الأكاديمية.</p><form onSubmit={e => void submit(e)}><label className="field">البريد الإلكتروني<input name="email" type="email" required autoComplete="username" /></label><label className="field">كلمة المرور<input name="password" type="password" required autoComplete="current-password" /></label><label className="field">رمز التحقق<input name="code" type="text" inputMode="numeric" pattern="[0-9]{6}" maxLength={6} required autoComplete="one-time-code" dir="ltr" /></label><button className="primary-button" disabled={busy}>{busy ? "جارٍ التحقق…" : "دخول المنصة"}</button></form>{error && <p role="alert" className="error-message">{error}</p>}<a href="/login">دخول الأكاديمية</a></section></main>;
}
