export type FamilyCashBody = { quoteHash: string; lines: { sportEnrollmentId: string; subscriptionPlanId: string }[] } & ({ guardianId: string } | { reference: string });
export type PendingCashFamily = { key: string; body: FamilyCashBody };
const storageKey = (session: string) => `academy-cash-family-pending:${session}`;

// Only the reviewed IDs, fingerprint and actor-bound protected reference are
// stored. Never persist family names, telephone numbers or amounts here.
export function rememberCashFamily(session: string, pending: PendingCashFamily) {
  try { sessionStorage.setItem(storageKey(session), JSON.stringify(pending)); }
  catch { throw new Error("تعذر حفظ استعادة التحصيل في المتصفح. لم نرسل الطلب؛ اسمح بتخزين الجلسة ثم حاول مجددًا."); }
}
export function recallCashFamily(session: string): PendingCashFamily | null {
  try {
    const value = JSON.parse(sessionStorage.getItem(storageKey(session)) ?? "null") as PendingCashFamily | null;
    if (!value || typeof value.key !== "string" || value.key.length < 8 || value.key.length > 100 || !value.body || typeof value.body.quoteHash !== "string" || value.body.quoteHash.length !== 64 || !Array.isArray(value.body.lines) || value.body.lines.length < 1 || value.body.lines.length > 20 || value.body.lines.some(l => typeof l.sportEnrollmentId !== "string" || typeof l.subscriptionPlanId !== "string")) return null;
    if ("reference" in value.body ? typeof value.body.reference !== "string" || !value.body.reference.startsWith("CPF1.") : !("guardianId" in value.body && typeof value.body.guardianId === "string")) return null;
    return value;
  } catch { return null; }
}
export function forgetCashFamily(session: string) {
  try { sessionStorage.removeItem(storageKey(session)); } catch { /* No financial side effect. */ }
}
