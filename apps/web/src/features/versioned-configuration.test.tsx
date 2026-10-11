import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { VersionedConfiguration } from "./versioned-configuration";
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const settings = { currency: "EGP", timeZone: "Africa/Cairo", features: { nutrition: true, community_ads: false }, cashEnabled: true, consecutiveAbsenceAlert: 3, tenantMarketingOptIn: false };
it("shows licensed features separately and creates a draft without activating it", async () => {
  const fetcher = vi.fn().mockImplementation(async (url, init) => ({ ok: true, status: 200, json: async () => url === "/api/v1/my-permissions" ? ["academy.settings.manage"] : url === "/api/v1/auth/csrf" ? { token: "csrf" } : init ? {} : { entitlements: [{ feature: "nutrition", enabled: true, planCode: "Core" }, { feature: "community_ads", enabled: false, planCode: "Core" }], versions: [{ id: "v1", revision: 1, version: 5, state: "Active", contentHash: "a".repeat(64), settings }], history: [] } }));
  global.fetch = fetcher; render(<VersionedConfiguration />);
  await screen.findByRole("checkbox", { name: "التغذية" });
  expect(screen.getByRole("checkbox", { name: /إعلانات المجتمع/ })).toBeDisabled();
  fireEvent.click(screen.getByRole("checkbox", { name: "التغذية" }));
  fireEvent.click(screen.getByRole("button", { name: "حفظ إصدار مسودة" }));
  await waitFor(() => expect(fetcher).toHaveBeenCalledWith("/api/v1/manage/configuration/drafts", expect.objectContaining({ method: "POST", body: expect.stringContaining('"nutrition":false') })));
  expect(fetcher.mock.calls.some(([url]) => String(url).includes("transition"))).toBe(false);
});
it("does not load configuration or expose controls without the server permission", async () => {
  const fetcher = vi.fn().mockResolvedValue({ ok: true, json: async () => [] }); global.fetch = fetcher;
  render(<VersionedConfiguration />); await waitFor(() => expect(fetcher).toHaveBeenCalledTimes(1));
  expect(screen.queryByRole("button", { name: "حفظ إصدار مسودة" })).not.toBeInTheDocument();
});
