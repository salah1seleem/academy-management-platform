import { apiProblemMessage, requestToken } from "../lib/api-problem";

export class PlatformSessionExpiredError extends Error {
  constructor() { super("انتهت جلسة إدارة المنصة. سجّل الدخول مجددًا ثم أعد الحفظ؛ بيانات النموذج باقية في هذه الصفحة."); }
}

export class PlatformTimeZoneLockedError extends Error {
  constructor(message: string, readonly timeZone: string) { super(message); }
}

export async function platformRequest(path: string, method: "POST" | "PUT", body?: unknown | FormData) {
  const { status, token } = await requestToken("/api/v1/platform/auth/csrf")
    .catch(() => { throw new Error("تعذر الاتصال بالمنصة. تحقّق من الشبكة وأعد المحاولة."); });
  if (status === 401) throw new PlatformSessionExpiredError();
  if (status === 403) throw new Error("حسابك لا يملك صلاحية تنفيذ هذا الإجراء في إدارة المنصة.");
  if (!token) throw new Error("تعذر بدء الطلب. حدّث الصفحة وأعد المحاولة.");
  const headers: Record<string, string> = { "X-CSRF-TOKEN": token };
  const multipart = body instanceof FormData;
  if (!multipart) headers["Content-Type"] = "application/json";
  const response = await fetch(path, { method, headers, body: multipart ? body : JSON.stringify(body ?? {}) })
    .catch(() => { throw new Error("انقطع الاتصال قبل وصول نتيجة الطلب. تحقّق من الحالة قبل إعادة المحاولة."); });
  if (!response.ok) {
    if (response.status === 401) throw new PlatformSessionExpiredError();
    if (response.status === 403) throw new Error("حسابك لا يملك صلاحية تنفيذ هذا الإجراء في إدارة المنصة.");
    const conflict = response.status === 409 ? await response.clone().json().catch(() => null) : null;
    const message = await apiProblemMessage(response, "تعذر تنفيذ الطلب في إدارة المنصة.");
    if (conflict?.code === "academy_time_zone_locked" && typeof conflict.timeZone === "string") {
      throw new PlatformTimeZoneLockedError(message, conflict.timeZone);
    }
    throw new Error(message);
  }
  return response;
}
