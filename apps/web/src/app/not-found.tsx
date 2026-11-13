import Link from "next/link";

export default function NotFound() {
  return <main className="center-shell">
    <section className="login-card" aria-labelledby="not-found-title">
      <h1 id="not-found-title">الصفحة غير موجودة</h1>
      <p>قد يكون الرابط قديمًا أو غير مكتمل.</p>
      <Link className="button button-primary" href="/">الصفحة الرئيسية</Link>
    </section>
  </main>;
}
