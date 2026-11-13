import { TechnicalPromotion } from "../../../../../../features/technical-promotion";

export default async function Page({ params, searchParams }: {
  params: Promise<{ id: string }>;
  searchParams: Promise<{ fromPlan?: string; includeDecided?: string }>;
}) {
  const { id } = await params;
  const search = await searchParams;
  const reviewQuery = search.fromPlan === "1" ? "?fromPlan=1" : search.includeDecided === "true" ? "?includeDecided=true" : "";
  return <TechnicalPromotion key={id} id={id} reviewQuery={reviewQuery} />;
}
