import { GuardianRequestList } from "../../../features/guardian-core";

export default async function Page({ searchParams }: { searchParams: Promise<{ created?: string | string[] }> }) {
  const { created } = await searchParams;
  return <GuardianRequestList created={typeof created === "string" ? created : undefined} />;
}
