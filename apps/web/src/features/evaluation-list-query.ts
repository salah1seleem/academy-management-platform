export const evaluationsPath = "/dashboard/evaluations";
export const publishedReportsPath = `${evaluationsPath}/reports`;
export const maxEvaluationSearchLength = 200;

type QueryValue = string | string[] | null | undefined;
const statuses = ["Draft", "Published", "PublishedInternal"];
const pageSizes = [10, 25, 50];
const uuidPattern = /^[a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12}$/i;

function uuid(value: QueryValue) {
  return typeof value === "string" && uuidPattern.test(value) ? value.toLowerCase() : "";
}

export function evaluationListScope(query: Record<string, QueryValue>) {
  return {
    playerId: uuid(query.playerId),
    groupId: uuid(query.groupId),
    search: typeof query.search === "string" ? query.search.slice(0, maxEvaluationSearchLength) : "",
    status: typeof query.status === "string" && statuses.includes(query.status) ? query.status : "",
    page: typeof query.page === "string" && /^[1-9]\d{0,2}$/.test(query.page) ? Number(query.page) - 1 : 0,
    pageSize: typeof query.pageSize === "string" && pageSizes.includes(Number(query.pageSize)) ? Number(query.pageSize) : 25,
  };
}

type EvaluationListScope = ReturnType<typeof evaluationListScope>;

export function evaluationListParams(scope: EvaluationListScope, publishedOnly: boolean) {
  const query = new URLSearchParams();
  if (scope.playerId) query.set("playerId", scope.playerId);
  if (scope.groupId) query.set("groupId", scope.groupId);
  if (scope.search) query.set("search", scope.search);
  if (scope.status && !publishedOnly) query.set("status", scope.status);
  if (scope.page > 0) query.set("page", String(scope.page + 1));
  if (scope.pageSize !== 25) query.set("pageSize", String(scope.pageSize));
  return query;
}

export function evaluationListHref(scope: EvaluationListScope, publishedOnly: boolean) {
  const query = evaluationListParams(scope, publishedOnly).toString();
  return (publishedOnly ? publishedReportsPath : evaluationsPath) + (query ? `?${query}` : "");
}

export function evaluationRowAnchor(id: string) {
  return `evaluation-${id.toLowerCase()}`;
}
