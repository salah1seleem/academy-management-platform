import { render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import GuardianPage from "./page";

vi.mock("next/navigation", () => ({ useRouter: () => ({ replace: vi.fn() }) }));

beforeEach(() => {
  global.fetch = vi.fn(() => new Promise<Response>(() => undefined));
});

describe("guardian subscription actions", () => {
  it("keeps the three fixed actions visible and separate", async () => {
    render(<GuardianPage />);
    expect(await screen.findByRole("navigation", { name: "إجراءات الاشتراك" })).toBeVisible();
    expect(screen.getByText(/اشتراك جديد/)).toBeVisible();
    expect(screen.getByRole("link", { name: "تجديد الاشتراك" })).toHaveAttribute("href", "#children");
    expect(screen.getByRole("link", { name: "تجديد اشتراك لغيره" })).toHaveAttribute("href", "/guardian/renew-for-another");
  });
});
