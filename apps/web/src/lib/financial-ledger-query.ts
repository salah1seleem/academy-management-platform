export function financialLedgerScope(search: Pick<URLSearchParams, "get">) {
  const scope = new URLSearchParams();
  for (const key of ["from", "to", "allDates", "sportId", "branchId", "planId", "provider", "search", "collectionType", "subscriptionKind", "externalKind", "page"]) {
    const value = search.get(key);
    if (value) scope.set(key, value);
  }
  return scope;
}
