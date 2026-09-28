export async function csrfRequest(path: string, method: "PUT" | "POST", body: unknown) {
  const csrfResponse = await fetch("/api/v1/auth/csrf");
  if (!csrfResponse.ok) throw new Error("تعذر بدء الطلب الآمن.");
  const { token } = await csrfResponse.json() as { token: string };
  const response = await fetch(path, { method, headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token }, body: JSON.stringify(body) });
  if (!response.ok) {
    const problem = await response.json().catch(() => null) as { message?: string; detail?: string } | null;
    throw new Error(problem?.message ?? problem?.detail ?? "تعذر حفظ التغييرات.");
  }
  return response;
}
