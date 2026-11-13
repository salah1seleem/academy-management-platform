import { requestToken } from "./api-problem";

const networkError = "تعذر الاتصال. تحقّق من الشبكة وأعد المحاولة.";
const rateLimit = "محاولات كثيرة. انتظر قليلًا ثم أعد المحاولة.";

export async function webAuthRequest(
  base: "/api/v1/auth" | "/api/v1/platform/auth",
  action: "login" | "logout" | "guardian/otp/request" | "guardian/otp/verify",
  body?: object,
) {
  const { status, token } = await requestToken(`${base}/csrf`).catch(() => { throw new Error(networkError); });
  if (status === 429) throw new Error(rateLimit);
  if (!token) throw new Error("تعذر بدء الطلب. حدّث الصفحة وأعد المحاولة.");

  const headers: Record<string, string> = { "X-CSRF-TOKEN": token };
  if (body) headers["Content-Type"] = "application/json";
  const response = await fetch(`${base}/${action}`, { method: "POST", headers, body: body ? JSON.stringify(body) : undefined })
    .catch(() => { throw new Error(networkError); });
  if (response.status === 429) throw new Error(rateLimit);
  return response;
}
