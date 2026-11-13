import { apiProblem, apiProblemMessage, apiTransportError, requestToken } from "../lib/api-problem";

export function announceSaveSuccess(message: string) {
  window.dispatchEvent(new CustomEvent("academy-save-success", { detail: message }));
}

export class DashboardRequestError extends Error {
  constructor(message: string, readonly status: number, readonly code?: string) { super(message); }
}

export async function csrfRequest(path: string, method: "PUT" | "POST", body: unknown, extraHeaders: Record<string, string> = {}, options: { announce?: boolean; signal?: AbortSignal } = {}) {
  const { token, status } = await requestToken("/api/v1/auth/csrf", options.signal)
    .catch(cause => { throw apiTransportError(cause, "تعذر الاتصال. تحقّق من الشبكة وأعد المحاولة."); });
  if (!token) throw new DashboardRequestError("تعذر بدء الطلب. حدّث الصفحة وأعد المحاولة.", status);
  const preview = /(?:^|[-/])(?:preview|quote|search|resolve|placement-recommendations)\/?$/.test(path.split("?")[0]);
  const unavailable = preview ? "تعذر تحميل نتيجة الطلب. أعد المحاولة." : "تعذر تأكيد الحفظ. تحقّق من الحالة قبل إعادة المحاولة.";
  const response = await fetch(path, { method, headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token, ...extraHeaders }, body: JSON.stringify(body), ...(options.signal && { signal: options.signal }) })
    .catch(cause => { throw apiTransportError(cause, unavailable); });
  if (!response.ok) {
    const fallback = response.status >= 500 ? unavailable : ({
      401: "انتهت الجلسة. سجّل الدخول مجددًا.",
      403: "لا تملك صلاحية تنفيذ هذا الإجراء.",
      404: "هذا السجل غير متاح.",
      409: "تغيّرت البيانات. حدّث الصفحة وراجعها قبل إعادة المحاولة.",
      429: "طلبات كثيرة في وقت قصير. انتظر قليلًا ثم أعد المحاولة.",
    } as Record<number, string>)[response.status] ?? "راجع البيانات المدخلة وأعد المحاولة.";
    const problem = await apiProblem(response, fallback);
    throw new DashboardRequestError(problem.message, response.status, problem.code);
  }
  if (options.announce !== false && !preview) {
    announceSaveSuccess(method === "PUT" ? "تم حفظ التعديلات." : response.status === 201 ? "تم الإنشاء." : "تم الحفظ.");
  }
  return response;
}

export async function uploadAsset(file: File, purpose: "Photo" | "Media" | "Proof" | "News" | "StaffPhoto" | "Campaign", target: { playerId?: string; groupId?: string; caption?: string; eventOn?:string; sessionId?:string; privacyConfirmed?:string; subjectPlayerIds?:string; noRecognizableChildren?:string } = {}) {
  const { token } = await requestToken("/api/v1/auth/csrf")
    .catch(() => { throw new Error("تعذر الاتصال. تحقّق من الشبكة وأعد المحاولة."); });
  if (!token) throw new Error("تعذر بدء رفع الملف. حدّث الصفحة وأعد المحاولة.");
  const data = new FormData(); data.set("file", file); data.set("purpose", purpose);
  Object.entries(target).forEach(([key, value]) => { if (value) data.set(key, value); });
  const response = await fetch(purpose === "News" ? "/api/v1/content/news/image" : "/api/v1/assets/upload", { method: "POST", headers: { "X-CSRF-TOKEN": token }, body: data })
    .catch(() => { throw new DashboardRequestError("تعذر تأكيد رفع الملف. تحقّق من الاتصال.", 0); });
  if (!response.ok) {
    const problem = purpose === "Media" && response.status === 503 ? await response.clone().json().catch(() => null) : null;
    throw new DashboardRequestError(await apiProblemMessage(response, "تعذر رفع الملف."), response.status,
      problem?.code === "media_inspection_unavailable" ? problem.code : undefined);
  }
  return response.json().catch(() => {
    throw new DashboardRequestError("تعذر تأكيد رفع الملف.", 0);
  }) as Promise<{ id: string; url: string; contentType: string }>;
}
