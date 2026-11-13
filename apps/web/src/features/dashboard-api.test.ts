import { afterEach, describe, expect, it, vi } from "vitest";
import { csrfRequest, DashboardRequestError, uploadAsset } from "./dashboard-api";

const csrf = () => Response.json({ token: "test-token" });
afterEach(() => vi.unstubAllGlobals());

describe("dashboard request feedback", () => {
  it("preserves actionable Arabic validation without showing framework output", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(csrf()).mockResolvedValueOnce(Response.json({
      title: "One or more validation errors occurred.",
      errors: { date: ["اختر موعدًا بعد اليوم."], payload: ["The value could not be converted to System.DateOnly."] },
    }, { status: 422 })));
    await expect(csrfRequest("/api/v1/test", "PUT", {})).rejects.toMatchObject({
      status: 422, message: "اختر موعدًا بعد اليوم.",
    });
  });

  it("uses a safe message for server errors and does not announce a save", async () => {
    vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(csrf()).mockResolvedValueOnce(Response.json({
      message: "خطأ من الخادم: database connection failed",
    }, { status: 500 })));
    const saved = vi.fn();
    window.addEventListener("academy-save-success", saved);
    try {
      await expect(csrfRequest("/api/v1/test", "POST", {})).rejects.toMatchObject({
        status: 500, message: "تعذر تأكيد الحفظ. تحقّق من الحالة قبل إعادة المحاولة.",
      });
      expect(saved).not.toHaveBeenCalled();
    } finally { window.removeEventListener("academy-save-success", saved); }
  });

  it("does not retry a write whose response was lost or misreport it as a rejected write", async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(csrf()).mockRejectedValueOnce(new TypeError("Failed to fetch"));
    vi.stubGlobal("fetch", fetcher);
    const error = await csrfRequest("/api/v1/test", "POST", {}, { "Idempotency-Key": "same-intent" }).catch(value => value);
    expect(error).not.toBeInstanceOf(DashboardRequestError);
    expect(error.message).toContain("تحقّق من الحالة");
    expect(fetcher).toHaveBeenCalledTimes(2);
  });

  it("announces a saved request but never a quote or lookup", async () => {
    vi.stubGlobal("fetch", vi.fn(async input => String(input).endsWith("/csrf") ? csrf() : Response.json({ id: "sample" })));
    const saved = vi.fn();
    window.addEventListener("academy-save-success", saved);
    try {
      await csrfRequest("/api/v1/finance/cash/collection-quote", "POST", {});
      await csrfRequest("/api/v1/finance/cash/cross-branch/search?branch=demo", "POST", {});
      await csrfRequest("/api/v1/manage/groups/merge/preview", "POST", {});
      expect(saved).not.toHaveBeenCalled();
      await csrfRequest("/api/v1/manage/groups/merge", "POST", {});
      expect(saved).toHaveBeenCalledOnce();
    } finally { window.removeEventListener("academy-save-success", saved); }
  });

  it("does not upload when a proxy sends HTML instead of a CSRF token", async () => {
    const fetcher = vi.fn().mockResolvedValueOnce(new Response("<html>Login</html>"));
    vi.stubGlobal("fetch", fetcher);
    await expect(uploadAsset(new File(["demo"], "demo.png"), "News")).rejects.toThrow("تعذر بدء رفع الملف");
    expect(fetcher).toHaveBeenCalledOnce();
  });
});
