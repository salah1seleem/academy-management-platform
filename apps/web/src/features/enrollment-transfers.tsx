import { ArrowLeftRight } from "lucide-react";
import { formatDateAr } from "../lib/formatters";
export type EnrollmentTransfer = { effectiveDate: string; group?: string; groupName?: string };
export function EnrollmentTransfers({ transfers, scheduled = false }: { transfers?: EnrollmentTransfer[]; scheduled?: boolean }) {
  if (!transfers?.length) return null;
  return <aside className="enrollment-transfers" aria-label={scheduled ? "نقل مجموعة مجدول" : "سجل نقل المجموعة"}><ArrowLeftRight size={18} aria-hidden="true" /><div><strong>{scheduled ? "نقل مجموعة مجدول" : "سجل نقل المجموعة"}</strong>{transfers.map(t => <small key={t.effectiveDate}>{t.group ?? t.groupName} · من {formatDateAr(t.effectiveDate)}</small>)}</div></aside>;
}
