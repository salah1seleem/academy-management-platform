import { GuardianSubscriptionStatus } from "../../../../../features/subscriptions";

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <GuardianSubscriptionStatus id={id} />;
}
