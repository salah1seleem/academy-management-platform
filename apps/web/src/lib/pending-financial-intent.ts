const fallback = new Map<string, { hash: string; key: string }>();

// Keep a pending request identity across retry/reload without storing names or amounts.
// Amount calculation and payload conflict checks remain exclusively server-side.
export async function pendingFinancialIntent(scope: string, payload: unknown) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(JSON.stringify(payload)));
  const hash = Array.from(new Uint8Array(digest), b => b.toString(16).padStart(2, "0")).join("");
  let prior = fallback.get(scope);
  try { const raw = sessionStorage.getItem(`academy-finance:${scope}`); if (raw) prior = JSON.parse(raw); } catch { /* Browser storage can be unavailable. */ }
  const value = prior?.hash === hash ? prior : { hash, key: crypto.randomUUID() };
  fallback.set(scope, value);
  try { sessionStorage.setItem(`academy-finance:${scope}`, JSON.stringify(value)); } catch { /* Retry still works in this page session. */ }
  return value.key;
}
export function clearFinancialIntent(scope: string) {
  fallback.delete(scope);
  try { sessionStorage.removeItem(`academy-finance:${scope}`); } catch { /* No stored credentials. */ }
}
