import { GuardianFeedbackInbox } from "../../../features/guardian-feedback";

export default async function FeedbackPage({ searchParams }: { searchParams: Promise<{ kind?: string; status?: string; coachId?: string; overdue?: string; branchId?: string; groupId?: string }> }) {
  const { kind, status, coachId, overdue, branchId, groupId } = await searchParams;
  return <GuardianFeedbackInbox initialGroupId={groupId && /^[0-9a-fA-F-]{36}$/.test(groupId) ? groupId : ""} key={`${kind ?? ""}:${status ?? ""}:${coachId ?? ""}:${overdue ?? ""}:${branchId ?? ""}:${groupId ?? ""}`} initialKind={kind === "Rating" || kind === "Complaint" || kind === "Suggestion" ? kind : ""}
    initialStatus={status === "Open" || status === "Reviewed" || status === "Resolved" || status === "Unresolved" ? status : ""}
    initialCoachId={coachId && /^[0-9a-fA-F-]{36}$/.test(coachId) ? coachId : ""} initialOverdue={overdue === "true"} initialBranchId={branchId && /^[0-9a-fA-F-]{36}$/.test(branchId) ? branchId : ""} />;
}
