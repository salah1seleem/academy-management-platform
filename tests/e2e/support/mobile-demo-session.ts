import { expect, type APIRequestContext } from "@playwright/test";

// Worker-memory only: a real Demo login is reused by independent read journeys.
// Never persist credentials, bypass OTP limits, or share sessions across origins.
const sessions = new Map<string, { token: string; expires: number }>();
// The product's shared-IP auth budget is deliberately unchanged. A rejected
// request has not executed; wait for the real fixed window, never mint a token.
export async function demoAuthPost(request: APIRequestContext, path: string, data: object,
  pause: (ms: number) => Promise<void> = ms => new Promise(resolve => setTimeout(resolve, ms))) {
  let response = await request.post(path, { data });
  for (let attempt = 0; response.status() === 429 && attempt < 2; attempt++) {
    await pause(31_000);
    response = await request.post(path, { data });
  }
  return response;
}
export async function mobileDemoToken(request: APIRequestContext, phoneNumber: string, deviceName: string): Promise<string> {
  const probe = await request.get("/api/v1/auth/csrf");
  expect(probe.status()).toBe(200);
  const origin = new URL(probe.url()).origin;
  const key = `${origin}:${phoneNumber}`;
  const cached = sessions.get(key);
  if (cached && cached.expires > Date.now() + 60_000) {
    // Ordinary authenticated read, not another call against the shared IP auth
    // quota. No writes and no weakening of the OTP/session authorization rules.
    const current = await request.get("/api/v1/my-matches", { headers: { Authorization: `Bearer ${cached.token}` } });
    if (current.status() === 401) sessions.delete(key);
    else {
      expect(current.status()).toBe(200);
      return cached.token;
    }
  }
  const requested = await demoAuthPost(request,"/api/v1/mobile/auth/otp/request", { phoneNumber });
  expect(requested.status()).toBe(202);
  const verified = await demoAuthPost(request,"/api/v1/mobile/auth/otp/verify", { phoneNumber, challengeId: (await requested.json()).challengeId, code: "246810", deviceName });
  expect(verified.status()).toBe(200);
  const credentials = await verified.json();
  expect(typeof credentials.accessToken).toBe("string");
  const expires = Date.parse(credentials.accessExpiresAtUtc);
  expect(Number.isFinite(expires)).toBe(true);
  sessions.set(key, { token: credentials.accessToken, expires });
  return credentials.accessToken;
}
