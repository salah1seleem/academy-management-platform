import { afterEach, describe, expect, it, vi } from "vitest";
import { platformRequest } from "./platform-api";

afterEach(() => vi.unstubAllGlobals());
describe("platform request boundary", () => {
  it("uses platform CSRF rather than ambient academy identity", async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(Response.json({ token: "platform-csrf" })).mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetcher);
    await platformRequest("/api/v1/platform/academies/one/branding", "PUT", { displayName: "تجريبي" });
    expect(fetcher.mock.calls[0][0]).toBe("/api/v1/platform/auth/csrf");
    expect(fetcher.mock.calls[1][1].headers["X-CSRF-TOKEN"]).toBe("platform-csrf");
    expect(fetcher.mock.calls.some(call => call[0] === "/api/v1/auth/csrf")).toBe(false);
  });
  it("uploads multipart without overriding browser boundary", async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(Response.json({ token: "token" })).mockResolvedValueOnce(new Response(null, { status: 204 }));
    vi.stubGlobal("fetch", fetcher);
    const body = new FormData(); body.set("file", new File(["synthetic"], "logo.png"));
    await platformRequest("/api/v1/platform/academies/one/logo", "POST", body);
    expect(fetcher.mock.calls[1][1].body).toBe(body);
    expect(fetcher.mock.calls[1][1].headers["Content-Type"]).toBeUndefined();
  });
  it("stops before write when CSRF unavailable and reports Arabic error", async () => {
    const fetcher = vi.fn().mockResolvedValue(new Response(null, { status: 401 })); vi.stubGlobal("fetch", fetcher);
    await expect(platformRequest("/api/v1/platform/academies", "POST", {})).rejects.toThrow("تعذر بدء الطلب الآمن للمنصة.");
    expect(fetcher).toHaveBeenCalledTimes(1);
  });
});
