export class PlatformSessionExpiredError extends Error {
  constructor() { super("انتهت جلسة إدارة المنصة. سجّل الدخول مجددًا ثم أعد الحفظ؛ بيانات النموذج باقية في هذه الصفحة."); }
}

export async function platformRequest(path: string, method: "POST" | "PUT", body?: unknown | FormData) {
  const tokenResponse = await fetch("/api/v1/platform/auth/csrf", { cache: "no-store" });
  if (!tokenResponse.ok) throw new Error("تعذر بدء الطلب الآمن للمنصة.");
  const { token } = await tokenResponse.json() as { token: string };
  const headers: Record<string, string> = { "X-CSRF-TOKEN": token };
  const multipart = body instanceof FormData;
  if (!multipart) headers["Content-Type"] = "application/json";
  const response = await fetch(path, { method, headers, body: multipart ? body : JSON.stringify(body ?? {}) });
  if (!response.ok) {
    if (response.status === 401) throw new PlatformSessionExpiredError();
    if (response.status === 403) throw new Error("حسابك لا يملك صلاحية تنفيذ هذا الإجراء في إدارة المنصة.");
    const problem = await response.json().catch(() => ({}));
    const message = problem.title ?? problem.message;
    const details = Array.isArray(problem.errors) ? problem.errors.filter((x: unknown) => typeof x === "string" && /[\u0600-\u06ff]/.test(x)).join(" ") : "";
    throw new Error((typeof message === "string" && /[\u0600-\u06ff]/.test(message) ? message : "تعذر تنفيذ عملية المنصة.") + (details ? " " + details : ""));
  }
  return response;
}
