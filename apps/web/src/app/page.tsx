"use client";

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";

type Me = { displayName: string; academyId: string; academyName: string; role: string };
type Membership = { academyId: string; academyName: string; role: string };
const roleNames: Record<string, string> = { AcademyOwner: "مالك الأكاديمية", AcademyAdmin: "إداري الأكاديمية", Coach: "مدرب", Guardian: "ولي أمر" };

async function csrf() {
  const response = await fetch("/api/v1/auth/csrf", { credentials: "include" });
  if (!response.ok) throw new Error("csrf");
  return (await response.json()) as { token: string };
}

export default function HomePage() {
  const router = useRouter();
  const [me, setMe] = useState<Me | null>(null);
  const [academies, setAcademies] = useState<Membership[]>([]);
  const [busy, setBusy] = useState(true);

  useEffect(() => {
    Promise.all([fetch("/api/v1/me"), fetch("/api/v1/my-academies")]).then(async ([meResponse, academiesResponse]) => {
      if (meResponse.status === 401) return router.replace("/login");
      if (!meResponse.ok || !academiesResponse.ok) throw new Error("تعذر تحميل الجلسة");
      setMe((await meResponse.json()) as Me);
      setAcademies((await academiesResponse.json()) as Membership[]);
      setBusy(false);
    }).catch(() => router.replace("/login"));
  }, [router]);

  async function selectAcademy(academyId: string) {
    const { token } = await csrf();
    const response = await fetch("/api/v1/session/academy", {
      method: "POST", headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token }, body: JSON.stringify({ academyId }),
    });
    if (response.ok) window.location.reload();
  }

  async function logout() {
    const { token } = await csrf();
    await fetch("/api/v1/auth/logout", { method: "POST", headers: { "X-CSRF-TOKEN": token } });
    router.replace("/login");
  }

  if (busy || !me) return <main className="center-shell"><p>جارٍ تحميل الحساب…</p></main>;
  return (
    <main className="page-shell">
      <section className="authenticated-card" aria-labelledby="welcome-title">
        <span className="eyebrow">الأساس الآمن — Slice 1</span>
        <h1 id="welcome-title">مرحباً، {me.displayName}</h1>
        <dl className="identity-summary">
          <div><dt>الأكاديمية الحالية</dt><dd>{me.academyName}</dd></div>
          <div><dt>الدور</dt><dd>{roleNames[me.role] ?? me.role}</dd></div>
        </dl>
        {academies.length > 1 && <label className="field">تغيير الأكاديمية
          <select value={me.academyId} onChange={(event) => void selectAcademy(event.target.value)}>
            {academies.map((academy) => <option key={academy.academyId} value={academy.academyId}>{academy.academyName}</option>)}
          </select>
        </label>}
        <p className="development-note">وحدات التشغيل ستصل في الشرائح التالية؛ لا توجد بيانات لاعبين أو اشتراكات في هذه المرحلة.</p>
        <button className="secondary-button" onClick={() => void logout()}>تسجيل الخروج</button>
      </section>
    </main>
  );
}
