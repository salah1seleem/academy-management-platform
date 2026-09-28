"use client";
import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
type Child = { id: string; playerCode: string; arabicName: string };
export default function GuardianPage() { const router = useRouter(); const [children, setChildren] = useState<Child[]>([]); useEffect(() => { fetch("/api/v1/guardian/children").then(async r => { if (!r.ok) return router.replace("/login"); setChildren(await r.json() as Child[]); }); }, [router]); return <main className="page-shell"><section className="authenticated-card"><span className="eyebrow">مساحة ولي الأمر</span><h1>الأبناء المرتبطون</h1><div className="data-list">{children.map(child => <article key={child.id}><div><strong>{child.arabicName}</strong><small>{child.playerCode}</small></div></article>)}</div><p className="development-note">تظهر الروابط الصريحة فقط. التقارير والاشتراكات والمحتوى تصل في شرائح لاحقة.</p></section></main>; }
