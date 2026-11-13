"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { ReactNode } from "react";
import { HelpEntry } from "./help-entry";

export function GuardianShell({ children }: { children: ReactNode }) {
  const path = usePathname();
  const inSection = (route: string) => path === route || path?.startsWith(`${route}/`);
  return <div className="guardian-app">
    <main className="guardian-app-content">{children}<HelpEntry guardian /></main>
    <nav className="guardian-bottom-nav" aria-label="تنقل ولي الأمر">
      <Link href="/guardian" aria-current={path === "/guardian" ? "page" : undefined}>الرئيسية</Link>
      <Link href="/guardian#children" aria-current={inSection("/guardian/children") || inSection("/guardian/evaluations") ? "true" : undefined}>الأبناء</Link>
      <Link href="/guardian/receipts" aria-current={inSection("/guardian/receipts") ? (path === "/guardian/receipts" ? "page" : "true") : undefined}>الإيصالات</Link>
      <Link href="/guardian/enrollment-requests" aria-current={inSection("/guardian/enrollment-requests") ? (path === "/guardian/enrollment-requests" ? "page" : "true") : undefined}>طلبات الاشتراك</Link>
    </nav>
  </div>;
}
