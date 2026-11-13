import { EvaluationReport } from "../../../../../features/evaluations";
import { evaluationListHref, evaluationListScope, evaluationRowAnchor } from "../../../../../features/evaluation-list-query";

export default async function Page({ params, searchParams }: {
  params: Promise<{ id: string }>;
  searchParams: Promise<Record<string, string | string[] | undefined>>;
}) {
  const [{ id }, query] = await Promise.all([params, searchParams]);
  const fromPublished = query.from === "published";
  const fromList = fromPublished || query.from === "list";
  const scope = evaluationListScope(fromList ? query : {});
  const back = {
    href: evaluationListHref(scope, fromPublished) + (fromList ? `#${evaluationRowAnchor(encodeURIComponent(id))}` : ""),
    label: fromPublished ? "العودة إلى التقارير المنشورة" : "العودة إلى تقييمات اللاعبين",
  };
  return <EvaluationReport id={id} staffBack={back} />;
}
