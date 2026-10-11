import { ArrowLeftRight } from "lucide-react";
import { formatDateAr } from "../lib/formatters";
export type EnrollmentTransfer = { effectiveDate: string; group?: string; groupName?: string };
export function EnrollmentTransfers({ transfers, moves, scheduled = false }: { transfers?: EnrollmentTransfer[]; moves?: { date: string; group: string }[]; scheduled?: boolean }) {
  if (!transfers?.length && !moves?.length) return null;
  return <>{!!transfers?.length && <aside className="enrollment-transfers" aria-label={scheduled ? "نقل مجموعة مجدول" : "سجل نقل المجموعة"}><ArrowLeftRight size={18} aria-hidden="true" /><div><strong>{scheduled ? "نقل مجموعة مجدول" : "سجل نقل المجموعة"}</strong>{transfers.map(t => <small key={t.effectiveDate}>{t.group ?? t.groupName} · من {formatDateAr(t.effectiveDate)}</small>)}</div></aside>}{!!moves?.length && <aside className="enrollment-transfers" aria-label="حصص منقولة"><ArrowLeftRight size={18} aria-hidden="true" /><div><strong>حصص منقولة</strong>{moves.map((m, i) => <small key={i}>{m.group} · {formatDateAr(m.date)}</small>)}</div></aside>}</>;
}
