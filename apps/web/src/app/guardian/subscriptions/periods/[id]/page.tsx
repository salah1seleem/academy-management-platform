import { GuardianSubscriptionStatus } from "../../../../../features/subscriptions";

export default async function Page({ params, searchParams }: {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ child?: string }>;
}) {
  const [{ id }, { child }] = await Promise.all([params, searchParams]);
  const childId = child && /^[a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12}$/i.test(child) ? child : undefined;
  return <GuardianSubscriptionStatus id={id} childId={childId} />;
}
