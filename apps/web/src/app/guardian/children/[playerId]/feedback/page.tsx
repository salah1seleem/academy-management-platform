import { GuardianFeedbackForm } from "../../../../../features/guardian-feedback-form";
export default async function Page({ params }: { params: Promise<{ playerId: string }> }) {
  const { playerId } = await params; return <GuardianFeedbackForm key={playerId} playerId={playerId}/>;
}
