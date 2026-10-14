import { GuardianFeedbackInbox } from "../../../features/guardian-feedback";

export default async function FeedbackPage({ searchParams }: { searchParams: Promise<{ kind?: string; status?: string; coachId?: string; overdue?: string; branchId?: string }> }) {
  const { kind, status, coachId, overdue, branchId } = await searchParams;
  return <GuardianFeedbackInbox key={`${kind ?? ""}:${status ?? ""}:${coachId ?? ""}:${overdue ?? ""}:${branchId ?? ""}`} initialKind={kind === "Rating" || kind === "Complaint" || kind === "Suggestion" ? kind : ""}
    initialStatus={status === "Open" || status === "Reviewed" || status === "Resolved" || status === "Unresolved" ? status : ""}
    initialCoachId={coachId && /^[0-9a-fA-F-]{36}$/.test(coachId) ? coachId : ""} initialOverdue={overdue === "true"} initialBranchId={branchId && /^[0-9a-fA-F-]{36}$/.test(branchId) ? branchId : ""} />;
}
