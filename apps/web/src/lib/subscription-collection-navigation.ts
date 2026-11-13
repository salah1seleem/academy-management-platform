export const collectionPath = "/dashboard/subscriptions/collections";

type CollectionScope = { term: string; page: number; pageSize: number };

export function collectionScope(search: Pick<URLSearchParams, "get">): CollectionScope {
  const size = search.get("pageSize");
  const page = search.get("page") ?? "";
  return {
    term: search.get("q")?.slice(0, 120) ?? "",
    page: /^[1-9]\d{0,5}$/.test(page) ? Number(page) : 1,
    pageSize: size === "10" || size === "50" ? Number(size) : 25,
  };
}

function collectionQuery({ term, page, pageSize }: CollectionScope) {
  const query = new URLSearchParams();
  if (term) query.set("q", term);
  if (page > 1) query.set("page", String(page));
  if (pageSize !== 25) query.set("pageSize", String(pageSize));
  return query;
}

export function collectionHref(scope: CollectionScope) {
  const query = collectionQuery(scope).toString();
  return query ? `${collectionPath}?${query}` : collectionPath;
}

export function collectionReceiptQuery(scope: CollectionScope) {
  const query = collectionQuery(scope);
  query.set("report", "collections");
  return query.toString();
}

export function receiptCollectionScope(search: Pick<URLSearchParams, "get">) {
  if (search.get("report") !== "collections") return null;
  const scope = collectionScope(search);
  return { href: collectionHref(scope), query: collectionReceiptQuery(scope) };
}
