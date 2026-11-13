type FinancialIntent = { hash: string; key: string; cashSessionId?: string };
const fallback = new Map<string, FinancialIntent>();

async function payloadHash(payload: unknown) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(JSON.stringify(payload)));
  return Array.from(new Uint8Array(digest), byte => byte.toString(16).padStart(2, "0")).join("");
}

function storedIntent(scope: string) {
  let intent = fallback.get(scope);
  try {
    const stored = sessionStorage.getItem(`academy-finance:${scope}`);
    if (stored) intent = JSON.parse(stored);
  } catch { /* The mounted page retains its in-memory intent. */ }
  return intent;
}

// Keep a pending request identity across retry/reload without storing names or amounts.
// Amount calculation and payload conflict checks remain exclusively server-side.
export async function pendingFinancialIntent(scope: string, payload: unknown, cashSessionId?: string) {
  const hash = await payloadHash(payload);
  const prior = storedIntent(scope);
  const value = { hash, key: prior?.hash === hash ? prior.key : crypto.randomUUID(), cashSessionId };
  fallback.set(scope, value);
  try { sessionStorage.setItem(`academy-finance:${scope}`, JSON.stringify(value)); } catch { /* Retry still works in this page session. */ }
  return value.key;
}
export function clearFinancialIntent(scope: string) {
  fallback.delete(scope);
  try { sessionStorage.removeItem(`academy-finance:${scope}`); } catch { /* No stored credentials. */ }
}

export function readFinancialIntentKey(scope: string) {
  const key: unknown = storedIntent(scope)?.key;
  return isUuid(key) ? key : null;
}

export async function matchesFinancialIntent(scope: string, key: string, payload: unknown) {
  const intent = storedIntent(scope);
  return intent?.key === key && intent.hash === await payloadHash(payload);
}

export function readFinancialIntentCashSession(scope: string, key: string) {
  const intent = storedIntent(scope);
  return intent?.key === key && isUuid(intent.cashSessionId) ? intent.cashSessionId : undefined;
}

function isUuid(value: unknown): value is string {
  return typeof value === "string" && /^[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}$/i.test(value);
}
