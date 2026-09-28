const roles = [
  { title: "مالك الأكاديمية", description: "معاينة لمساحة المتابعة والإعدادات." },
  { title: "الإداري", description: "معاينة لمساحة التشغيل اليومية." },
  { title: "المدرب", description: "معاينة لمساحة المجموعات والتدريب." },
  { title: "ولي الأمر", description: "معاينة لتجربة الأسرة على الهاتف." },
] as const;

export default function HomePage() {
  return (
    <main className="page-shell">
      <section className="hero" aria-labelledby="page-title">
        <span className="eyebrow">الأساس التقني — Slice 0</span>
        <h1 id="page-title">منصة إدارة الأكاديمية</h1>
        <p className="intro">
          واجهة عربية أولية، مهيأة للعمل كتطبيق ويب قابل للتثبيت على الهاتف.
        </p>
        <p className="development-note" role="note">
          معاينة واجهة فقط — لا يوجد تسجيل دخول أو صلاحيات مفعلة في هذه المرحلة.
        </p>
      </section>

      <section aria-labelledby="roles-title">
        <div className="section-heading">
          <h2 id="roles-title">معاينات الأدوار</h2>
          <span>غير تفاعلية</span>
        </div>
        <div className="role-grid">
          {roles.map((role) => (
            <article className="role-card" key={role.title}>
              <div className="role-icon" aria-hidden="true">
                {role.title.charAt(0)}
              </div>
              <div>
                <h3>{role.title}</h3>
                <p>{role.description}</p>
              </div>
            </article>
          ))}
        </div>
      </section>

      <footer>نسخة تأسيسية للتطوير المحلي</footer>
    </main>
  );
}
