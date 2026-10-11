export async function platformRequest(path: string, method: "POST" | "PUT", body?: unknown | FormData) {
  const tokenResponse = await fetch("/api/v1/platform/auth/csrf", { cache: "no-store" });
  if (!tokenResponse.ok) throw new Error("تعذر بدء الطلب الآمن للمنصة.");
  const { token } = await tokenResponse.json() as { token: string };
  const headers: Record<string, string> = { "X-CSRF-TOKEN": token };
  const multipart = body instanceof FormData;
  if (!multipart) headers["Content-Type"] = "application/json";
  const response = await fetch(path, { method, headers, body: multipart ? body : JSON.stringify(body ?? {}) });
  if (!response.ok) {
    const problem = await response.json().catch(() => ({}));
    throw new Error(problem.title ?? problem.message ?? "تعذر تنفيذ عملية المنصة.");
  }
  return response;
}
