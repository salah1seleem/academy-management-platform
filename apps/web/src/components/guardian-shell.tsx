import Link from "next/link";
import { ReactNode } from "react";

export function GuardianShell({ children }: { children: ReactNode }) {
  return <main className="guardian-app"><div className="guardian-app-content">{children}</div><nav className="guardian-bottom-nav" aria-label="تنقل ولي الأمر"><Link href="/guardian">الرئيسية</Link><Link href="/guardian#children">الأبناء</Link><Link href="/guardian/receipts">الإيصالات</Link><Link href="/guardian/enrollment-requests">طلبات الاشتراك</Link></nav></main>;
}
