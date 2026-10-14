import { GuardianFeedbackInbox } from "../../../features/guardian-feedback";

export default async function FeedbackPage({ searchParams }: { searchParams: Promise<{ kind?: string; status?: string; coachId?: string }> }) {
  const { kind, status, coachId } = await searchParams;
  return <GuardianFeedbackInbox key={`${kind ?? ""}:${status ?? ""}:${coachId ?? ""}`} initialKind={kind === "Rating" || kind === "Complaint" || kind === "Suggestion" ? kind : ""}
    initialStatus={status === "Open" || status === "Reviewed" || status === "Resolved" || status === "Unresolved" ? status : ""}
    initialCoachId={coachId && /^[0-9a-fA-F-]{36}$/.test(coachId) ? coachId : ""} />;
}
