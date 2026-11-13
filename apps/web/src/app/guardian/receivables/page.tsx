import Link from "next/link";
import { ArrowRight } from "lucide-react";
import { GuardianShell } from "../../../components/guardian-shell";
import { GuardianReceivables } from "../../../features/receivables";

export default function Page() {
  return <GuardianShell>
    <header className="guardian-page-header guardian-receipt-header">
      <Link href="/guardian/notifications"><ArrowRight size={18} aria-hidden />العودة إلى التنبيهات</Link>
      <h1>أرصدة الأسرة</h1>
    </header>
    <GuardianReceivables />
  </GuardianShell>;
}
