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
  const [mode, setMode] = useState<"staff" | "guardian">("staff");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [challengeId, setChallengeId] = useState("");
  const [phone, setPhone] = useState("");

  async function staffLogin(event: FormEvent<HTMLFormElement>) {
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

  async function guardianSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); setBusy(true); setError("");
    const data = new FormData(event.currentTarget);
    try {
      const { token } = await csrf();
      const path = challengeId ? "/api/v1/auth/guardian/otp/verify" : "/api/v1/auth/guardian/otp/request";
      const body = challengeId ? { challengeId, phoneNumber: phone, code: data.get("code") } : { phoneNumber: phone };
      const response = await fetch(path, { method: "POST", headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token }, body: JSON.stringify(body) });
      if (!response.ok) throw new Error("تعذر التحقق من رقم الهاتف أو الرمز.");
      if (challengeId) return router.replace("/guardian");
      const result = (await response.json()) as { challengeId: string };
      setChallengeId(result.challengeId); setBusy(false);
    } catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر المتابعة."); setBusy(false); }
  }

  return <main className="center-shell">
    <section className="login-card" aria-labelledby="login-title">
      <span className="eyebrow">منصة إدارة الأكاديمية</span><h1 id="login-title">تسجيل الدخول</h1>
      <div className="tabs" role="tablist" aria-label="نوع الحساب">
        <button className={mode === "staff" ? "active" : ""} onClick={() => setMode("staff")}>فريق الأكاديمية</button>
        <button className={mode === "guardian" ? "active" : ""} onClick={() => setMode("guardian")}>ولي أمر</button>
      </div>
      {mode === "staff" ? <form onSubmit={(event) => void staffLogin(event)}>
        <label className="field">البريد الإلكتروني<input name="email" type="email" autoComplete="username" required /></label>
        <label className="field">كلمة المرور<input name="password" type="password" autoComplete="current-password" required /></label>
        <button className="primary-button" disabled={busy}>{busy ? "جارٍ الدخول…" : "دخول آمن"}</button>
      </form> : <form onSubmit={(event) => void guardianSubmit(event)}>
        <p className="demo-note">بيئة العرض فقط: لا يتم إرسال رسالة SMS حقيقية.</p>
        <label className="field">رقم الهاتف<input dir="ltr" value={phone} onChange={(event) => setPhone(event.target.value)} placeholder="01000000001" required disabled={Boolean(challengeId)} /></label>
        {challengeId && <label className="field">رمز التحقق<input dir="ltr" name="code" inputMode="numeric" required /></label>}
        <button className="primary-button" disabled={busy}>{challengeId ? "تحقق ودخول" : "طلب رمز تجريبي"}</button>
      </form>}
      {error && <p className="error-message" role="alert">{error}</p>}
    </section>
  </main>;
}
