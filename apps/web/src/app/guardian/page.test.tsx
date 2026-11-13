import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import GuardianPage from "./page";

vi.mock("next/navigation", () => ({ usePathname: () => "/guardian", useRouter: () => ({ replace: vi.fn() }) }));
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

beforeEach(() => {
  global.fetch = vi.fn(async url => new Response(JSON.stringify(String(url).endsWith("/receivables") ? { outstanding: "0.00", overpaymentCredit: "0.00", items: [] } : { academyName: "أكاديمية النجوم الرياضية", guardianName: "سارة محمود", today: "2026-09-28", children: [], sportContexts: [] }), { status: 200 }));
});

describe("guardian subscription actions", () => {
  it("keeps the three fixed actions visible and separate", async () => {
    render(<GuardianPage />);
    expect(await screen.findByRole("navigation", { name: "إجراءات الاشتراك" })).toBeVisible();
    expect(screen.getByRole("link", { name: /اشتراك جديد/ })).toHaveAttribute("href", "/guardian/enrollment-requests/new");
    expect(screen.getByRole("link", { name: /تجديد الاشتراك$/ })).toHaveAttribute("href", "#children");
    expect(screen.getByRole("link", { name: /تجديد اشتراك لغيره/ })).toHaveAttribute("href", "/guardian/renew-for-another");
    expect(screen.getByText("لا يوجد أبناء مرتبطون بهذا الحساب.")).toBeVisible();
    expect(screen.queryByText(/0 ملفات/)).not.toBeInTheDocument();
  });

  it.each([[1, "ملف واحد"], [2, "ملفان"], [3, "3 ملفات"], [11, "11 ملفًا"]])(
    "uses natural Arabic for %i linked profiles",
    async (count, label) => {
      const initialFetch = vi.mocked(global.fetch).getMockImplementation()!;
      vi.mocked(global.fetch).mockImplementation((url, options) => {
        if (String(url) !== "/api/v1/guardian/home") return initialFetch(url, options);
        return Promise.resolve(new Response(JSON.stringify({
          academyName: "أكاديمية النجوم الرياضية",
          guardianName: "سارة محمود",
          today: "2026-09-28",
          children: Array.from({ length: count }, (_, index) => ({
            id: String(index), arabicName: `لاعب تجريبي ${index}`, dateOfBirth: "2016-09-28", sports: [],
          })),
          sportContexts: [],
        })));
      });
      render(<GuardianPage />);
      expect(await screen.findByText(label)).toBeVisible();
    },
  );
});
