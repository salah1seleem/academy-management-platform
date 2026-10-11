export function announceSaveSuccess(message: string) {
  window.dispatchEvent(new CustomEvent("academy-save-success", { detail: message }));
}

export async function csrfRequest(path: string, method: "PUT" | "POST", body: unknown, extraHeaders: Record<string, string> = {}) {
  const csrfResponse = await fetch("/api/v1/auth/csrf");
  if (!csrfResponse.ok) throw new Error("تعذر بدء الطلب الآمن.");
  const { token } = await csrfResponse.json() as { token: string };
  const response = await fetch(path, { method, headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token, ...extraHeaders }, body: JSON.stringify(body) });
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { message?: string; detail?: string; title?: string; errors?: Record<string, string[]> } | null;
    const validation = problem?.errors ? Object.values(problem.errors).flat().join(" ") : "";
    throw new Error(problem?.message ?? problem?.detail ?? (validation || problem?.title || "تعذر حفظ التغييرات."));
  }
  if (!path.endsWith("registration-quote") && !path.endsWith("/external/resolve") && !path.endsWith("/external/search") && !path.endsWith("/timetable/preview") && !path.endsWith("/placement/preview") && !path.endsWith("/placement-recommendations")) {
    announceSaveSuccess(method === "PUT" ? "تم حفظ التعديلات بنجاح." : response.status === 201 ? "تم الإنشاء بنجاح." : "تم حفظ العملية بنجاح.");
  }
  return response;
}

export async function uploadAsset(file: File, purpose: "Photo" | "Media" | "Proof" | "News" | "StaffPhoto", target: { playerId?: string; groupId?: string; caption?: string } = {}) {
  const csrf = await fetch("/api/v1/auth/csrf");
  if (!csrf.ok) throw new Error("تعذر بدء الطلب الآمن.");
  const { token } = await csrf.json() as { token: string };
  const data = new FormData(); data.set("file", file); data.set("purpose", purpose);
  Object.entries(target).forEach(([key, value]) => { if (value) data.set(key, value); });
  const response = await fetch("/api/v1/assets/upload", { method: "POST", headers: { "X-CSRF-TOKEN": token }, body: data });
  if (!response.ok) { const result = await response.json().catch(() => ({})); throw new Error(result.message ?? "تعذر رفع الملف."); }
  return response.json() as Promise<{ id: string; url: string; contentType: string }>;
}
