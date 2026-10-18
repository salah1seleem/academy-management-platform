export function announceSaveSuccess(message: string) {
  window.dispatchEvent(new CustomEvent("academy-save-success", { detail: message }));
}

export class DashboardRequestError extends Error {
  constructor(message: string, readonly status: number) { super(message); }
}

export async function csrfRequest(path: string, method: "PUT" | "POST", body: unknown, extraHeaders: Record<string, string> = {}, options: { announce?: boolean } = {}) {
  const csrfResponse = await fetch("/api/v1/auth/csrf");
  if (!csrfResponse.ok) throw new DashboardRequestError("تعذر بدء الطلب الآمن.", csrfResponse.status);
  const { token } = await csrfResponse.json() as { token: string };
  const response = await fetch(path, { method, headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token, ...extraHeaders }, body: JSON.stringify(body) });
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { message?: string; detail?: string; title?: string; errors?: Record<string, string[]> } | null;
    const validation = problem?.errors ? Object.values(problem.errors).flat().join(" ") : "";
    throw new DashboardRequestError(problem?.message ?? problem?.detail ?? (validation || problem?.title || "تعذر حفظ التغييرات."), response.status);
  }
  if (options.announce !== false && !path.endsWith("registration-quote") && !path.endsWith("/resolve") && !path.endsWith("/external/search") && !path.endsWith("/preview") && !path.endsWith("/placement-recommendations")) {
    announceSaveSuccess(method === "PUT" ? "تم حفظ التعديلات بنجاح." : response.status === 201 ? "تم الإنشاء بنجاح." : "تم حفظ العملية بنجاح.");
  }
  return response;
}

export async function uploadAsset(file: File, purpose: "Photo" | "Media" | "Proof" | "News" | "StaffPhoto" | "Campaign", target: { playerId?: string; groupId?: string; caption?: string; eventOn?:string; sessionId?:string; privacyConfirmed?:string; subjectPlayerIds?:string; noRecognizableChildren?:string } = {}) {
  const csrf = await fetch("/api/v1/auth/csrf");
  if (!csrf.ok) throw new Error("تعذر بدء الطلب الآمن.");
  const { token } = await csrf.json() as { token: string };
  const data = new FormData(); data.set("file", file); data.set("purpose", purpose);
  Object.entries(target).forEach(([key, value]) => { if (value) data.set(key, value); });
  const response = await fetch("/api/v1/assets/upload", { method: "POST", headers: { "X-CSRF-TOKEN": token }, body: data });
  if (!response.ok) { const result = await response.json().catch(() => ({})); throw new Error(result.message ?? "تعذر رفع الملف."); }
  return response.json() as Promise<{ id: string; url: string; contentType: string }>;
}
