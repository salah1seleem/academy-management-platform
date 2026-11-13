"use client";

import Link from "next/link";

export default function RouteError({ retry }: { retry: () => void }) {
  return <main className="center-shell">
    <section className="login-card" aria-labelledby="route-error-title">
      <h1 id="route-error-title">تعذر فتح الصفحة</h1>
      <p>جرّب تحميل الصفحة مرة أخرى.</p>
      <div className="row-actions">
        <button className="button button-primary" type="button" onClick={retry}>إعادة التحميل</button>
        <Link className="button button-secondary" href="/">الصفحة الرئيسية</Link>
      </div>
    </section>
  </main>;
}
