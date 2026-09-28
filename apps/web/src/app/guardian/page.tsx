"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { GuardianAttendanceList } from "../../features/attendance";
type Period = { plan: string; startDate: string; endDate?: string; remainingSessions?: number; status: string };
type Enrollment = { id: string; playerId: string; player: string; sport: string; period?: Period };
export default function GuardianPage() {
  const router = useRouter(); const [items, setItems] = useState<Enrollment[]>([]);
  useEffect(() => { fetch("/api/v1/guardian/subscriptions/enrollments").then(async r => { if (!r.ok) return router.replace("/login"); setItems(await r.json() as Enrollment[]); }); }, [router]);
  const children = Array.from(Map.groupBy(items, item => item.playerId).values());
  return <main className="page-shell guardian-wide"><section className="authenticated-card"><span className="eyebrow">مساحة ولي الأمر · اشتراكات الأبناء</span><h1>الأبناء المرتبطون</h1><nav className="guardian-actions" aria-label="إجراءات الاشتراك"><span aria-disabled="true">اشتراك جديد <small>في مرحلة لاحقة</small></span><a href="#children">تجديد الاشتراك</a><Link href="/guardian/renew-for-another">تجديد اشتراك لغيره</Link></nav><div id="children" className="data-list child-subscriptions">{children.map(enrollments => <article key={enrollments[0].playerId}><strong>{enrollments[0].player}</strong><div>{enrollments.map(item => <section className="sport-subscription" key={item.id}><div><b>{item.sport}</b><small>{item.period ? `${item.period.plan} · ${item.period.status} · ${item.period.startDate}${item.period.endDate ? ` ← ${item.period.endDate}` : ""}${item.period.remainingSessions != null ? ` · ${item.period.remainingSessions} حصة` : ""}` : "لا يوجد اشتراك مدفوع"}</small></div><Link className="primary-action" href={`/guardian/renew/${item.id}`}>تجديد الاشتراك</Link></section>)}</div><GuardianAttendanceList playerId={enrollments[0].playerId} /></article>)}</div></section></main>;
}
