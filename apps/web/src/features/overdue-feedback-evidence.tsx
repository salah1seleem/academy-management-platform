import Link from "next/link";
export type OverdueFeedbackData = { complaintCount: number; suggestionCount: number; submitterCount: number; oldestDueOn: string | null; asOfDate: string };
export function OverdueFeedbackReview({ data }: { data?: OverdueFeedbackData | null }) {
  return <div aria-label="ملخص الملاحظات المتأخرة">{data ? <><p>{data.complaintCount} شكوى · {data.suggestionCount} اقتراح · {data.submitterCount} حساب مقدم فريد</p>
    <p>أقدم موعد متابعة: <bdi>{data.oldestDueOn ?? "غير محدد"}</bdi></p></> : <p>بيانات الملاحظات غير متاحة حاليًا.</p>}
    <small>تجاوزت موعدًا حدده المالك؛ ليست اتهامًا أو رسالة أو تسوية مالية.</small></div>;
}
export function OverdueFeedbackLinks({ ids, groupId }: { ids?: string[] | null; groupId: string }) {
  return <>{ids?.slice(0, 3).map((id, i) => <p key={id}><Link href={`/dashboard/feedback/${id}`}>فتح ملاحظة من دليل الإنشاء {i + 1}</Link></p>)}
    <p><Link href={`/dashboard/feedback?groupId=${groupId}&status=Unresolved&overdue=true`}>كل الملاحظات المتأخرة للمجموعة نفسها</Link></p>
    <small>أول 3 ملاحظات من الدليل؛ إغلاق المتابعة لا يحل ملاحظة أو يرسل ردًا.</small></>;
}
