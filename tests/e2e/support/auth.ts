import { Page } from "@playwright/test";

export async function guardianLogin(page: Page, phoneNumber = "01000000001") {
  await page.context().clearCookies();
  await page.goto("/login");
  await page.evaluate(async phone => {
    const csrf = async () => fetch("/api/v1/auth/csrf").then(response => response.json()) as Promise<{ token: string }>;
    const requestToken = await csrf();
    const requested = await fetch("/api/v1/auth/guardian/otp/request", {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": requestToken.token },
      body: JSON.stringify({ phoneNumber: phone }),
    });
    if (!requested.ok) throw new Error("Guardian OTP request failed");
    const challenge = await requested.json() as { challengeId: string };
    const verifyToken = await csrf();
    const verified = await fetch("/api/v1/auth/guardian/otp/verify", {
      method: "POST",
      headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": verifyToken.token },
      body: JSON.stringify({ challengeId: challenge.challengeId, phoneNumber: phone, code: "246810" }),
    });
    if (!verified.ok) throw new Error("Guardian OTP verification failed");
  }, phoneNumber);
  await page.goto("/guardian");
}
