// Only the four existing write forms exposed by the Owner app may resume after
// sign-in. No arbitrary URLs, query strings, credentials or context bypass.
const operations = new Set([
  "/dashboard/players/new", "/dashboard/subscriptions/renew/new",
  "/dashboard/academy/coaches/create", "/dashboard/evaluations/new",
]);
export function operationReturnPath(value: string | null): string | null {
  return value && operations.has(value) ? value : null;
}
export function operationLoginPath(path: string): string {
  const next = operationReturnPath(path);
  return next ? `/login?returnTo=${encodeURIComponent(next)}` : "/login";
}
