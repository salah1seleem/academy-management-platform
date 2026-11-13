export const periodLists = {
  current: "الحالية",
  expiring: "تنتهي قريبًا",
  expired: "المنتهية",
} as const;
export type PeriodListKind = keyof typeof periodLists;

export function periodListScope(search: { get(name: string): string | null }) {
  const list = search.get("periodList");
  const kind: PeriodListKind = list === "expiring" || list === "expired" ? list : "current";
  return { kind, term: search.get("q")?.slice(0, 120) ?? "" };
}

export function periodListHref(kind: PeriodListKind, term: string) {
  return `/dashboard/subscriptions/${kind}${term ? `?${new URLSearchParams({ q: term })}` : ""}`;
}

export function periodScopeQuery(kind: PeriodListKind, term: string) {
  return new URLSearchParams({ periodList: kind, ...(term ? { q: term } : {}) });
}

export function receiptPeriodScope(search: { get(name: string): string | null }) {
  const id = search.get("periodId");
  if (!id || !/^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(id)) return null;
  const { kind, term } = periodListScope(search);
  const query = periodScopeQuery(kind, term);
  query.set("periodId", id);
  return { id, kind, term, query };
}
