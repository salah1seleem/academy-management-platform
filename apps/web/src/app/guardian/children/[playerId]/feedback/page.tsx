import { GuardianFeedbackForm } from "../../../../../features/guardian-feedback-form";
export default async function Page({ params, searchParams }: { params: Promise<{ playerId: string }>; searchParams?: Promise<{ kind?: string; groupId?: string; coachId?: string }> }) {
  const { playerId } = await params; const { kind, groupId, coachId } = await searchParams ?? {}; return <GuardianFeedbackForm key={`${playerId}:${kind}`} playerId={playerId} initialGroupId={groupId} initialCoachId={coachId} initialKind={kind==="Rating"?"Rating":"Complaint"}/>;
}
