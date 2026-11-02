import { SubscriptionPeriodDetail } from "../../../../../features/subscriptions";

export default async function Page({ params }: { params: Promise<{ id: string }> }) {
  const { id } = await params;
  return <SubscriptionPeriodDetail id={id} />;
}
