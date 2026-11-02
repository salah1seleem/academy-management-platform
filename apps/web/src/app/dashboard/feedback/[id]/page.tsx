import { GuardianFeedbackDetail } from "../../../../features/guardian-feedback-detail";
export default async function FeedbackDetailPage({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <GuardianFeedbackDetail key={id} id={id}/>;
}
