import { GuardianFeedbackInbox } from "../../../features/guardian-feedback";

export default async function FeedbackPage({ searchParams }: { searchParams: Promise<{ kind?: string; status?: string }> }) {
  const { kind, status } = await searchParams;
  return <GuardianFeedbackInbox key={`${kind ?? ""}:${status ?? ""}`} initialKind={kind === "Rating" || kind === "Complaint" || kind === "Suggestion" ? kind : ""}
    initialStatus={status === "Open" || status === "Reviewed" ? status : ""} />;
}
