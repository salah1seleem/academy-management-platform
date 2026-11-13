import { WorkContextChooser } from "../../../features/work-context";

export default async function WorkContextPage({ searchParams }: {
  searchParams: Promise<{ returnTo?: string | string[] }>;
}) {
  const { returnTo } = await searchParams;
  return <WorkContextChooser returnTo={typeof returnTo === "string" ? returnTo : undefined} />;
}
