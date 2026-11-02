type CashBody = { planId: string; quoteHash: string } & ({ enrollmentId: string } | { reference: string });
export type PendingCashCollection = { key: string; external: boolean; body: CashBody };
const storageKey = (session: string) => `academy-cash-pending:${session}`;

// No names, phone numbers, amounts or credentials. The protected payment
// reference still requires the original authenticated membership and shift.
export function rememberCashCollection(session: string, pending: PendingCashCollection) {
  try { sessionStorage.setItem(storageKey(session), JSON.stringify(pending)); }
  catch { throw new Error("تعذر حفظ استعادة المحاولة في المتصفح. لم نرسل التحصيل؛ اسمح بتخزين الجلسة ثم حاول مجددًا."); }
}
export function recallCashCollection(session: string): PendingCashCollection | null {
  try {
    const value = JSON.parse(sessionStorage.getItem(storageKey(session)) ?? "null") as PendingCashCollection | null;
    if (!value || typeof value.key !== "string" || value.key.length < 8 || value.key.length > 100 || typeof value.external !== "boolean" || !value.body || typeof value.body.planId !== "string" || typeof value.body.quoteHash !== "string" || value.body.quoteHash.length !== 64) return null;
    if (value.external ? !("reference" in value.body && typeof value.body.reference === "string" && value.body.reference.startsWith("CP1.")) : !("enrollmentId" in value.body && typeof value.body.enrollmentId === "string")) return null;
    return value;
  } catch { return null; }
}
export function forgetCashCollection(session: string) {
  try { sessionStorage.removeItem(storageKey(session)); } catch { /* No financial side effect. */ }
}
