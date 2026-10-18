import { PrivateInvestigations } from "../../../../../features/feedback-investigations";
export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  return <PrivateInvestigations id={(await params).id}/>;
}
