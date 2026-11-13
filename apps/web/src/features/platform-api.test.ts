import { afterEach, describe, expect, it, vi } from "vitest";
import { platformRequest, PlatformSessionExpiredError, PlatformTimeZoneLockedError } from "./platform-api";

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
    await expect(platformRequest("/api/v1/platform/academies", "POST", {})).rejects.toBeInstanceOf(PlatformSessionExpiredError);
    expect(fetcher).toHaveBeenCalledTimes(1);
  });
});

it("recognizes an expired session even when the server returns an empty 401 body", async () => {
 vi.stubGlobal("fetch",vi.fn().mockResolvedValueOnce(Response.json({token:"anonymous-csrf"})).mockResolvedValueOnce(new Response(null,{status:401})));
 await expect(platformRequest("/api/v1/platform/academies","POST",{})).rejects.toBeInstanceOf(PlatformSessionExpiredError);
});
it("explains denied platform authority without treating it as an expired session",async()=>{
 vi.stubGlobal("fetch",vi.fn().mockResolvedValueOnce(Response.json({token:"csrf"})).mockResolvedValueOnce(new Response(null,{status:403})));
 await expect(platformRequest("/api/v1/platform/academies","POST",{})).rejects.toThrow("لا يملك صلاحية");
});

it("preserves the saved zone and Arabic reason when a stale form is locked",async()=>{
 const message="المنطقة الزمنية ثابتة بعد ترحيل تحصيلات سابقة.";
 vi.stubGlobal("fetch",vi.fn().mockResolvedValueOnce(Response.json({token:"csrf"})).mockResolvedValueOnce(Response.json({code:"academy_time_zone_locked",message,timeZone:"Africa/Cairo"},{status:409})));
 await expect(platformRequest("/api/v1/platform/academies/one/setup","PUT",{})).rejects.toEqual(new PlatformTimeZoneLockedError(message,"Africa/Cairo"));
});
