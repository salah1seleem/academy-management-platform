import { GuardianFeedbackForm } from "../../../../../features/guardian-feedback-form";
export default async function Page({ params, searchParams }: { params: Promise<{ playerId: string }>; searchParams?: Promise<{kind?:string}> }) {
  const { playerId } = await params; const kind=(await searchParams)?.kind; return <GuardianFeedbackForm key={`${playerId}:${kind}`} playerId={playerId} initialKind={kind==="Rating"?"Rating":"Complaint"}/>;
}
