import { afterEach, expect, it, vi } from "vitest";
import { cleanup, render, screen } from "@testing-library/react";
import PlatformHome from "./page";

const router = vi.hoisted(() => ({ replace: vi.fn() }));
vi.mock("next/navigation", () => ({ useRouter: () => router }));
afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
  vi.clearAllMocks();
});

it("keeps a service failure on the access screen instead of redirecting to login", async () => {
  const fetcher = vi.fn(async () => new Response(null, { status: 503 }));
  vi.stubGlobal("fetch", fetcher);
  render(<PlatformHome />);
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر التحقق من حساب المنصة");
  expect(screen.getByRole("button", { name: "إعادة المحاولة" })).toBeEnabled();
  expect(router.replace).not.toHaveBeenCalled();
  expect(fetcher).toHaveBeenCalledTimes(1);
});

it.each([
  [503, "تعذر تحميل الأكاديميات", false],
  [403, "لا يملك صلاحية عرض الأكاديميات", false],
  [401, "انتهت جلسة إدارة المنصة", true],
])("offers recovery without a misleading zero count on directory HTTP %s", async (status, message, login) => {
  vi.stubGlobal("fetch", vi.fn(async (url: string) =>
    url.endsWith("/me")
      ? Response.json({})
      : new Response("<html>Service failure</html>", { status })));
  render(<PlatformHome />);
  expect(await screen.findByRole("alert")).toHaveTextContent(message);
  expect(screen.getByRole("button", { name: "إعادة تحميل الأكاديميات" })).toBeEnabled();
  expect(screen.queryByRole("navigation", { name: "صفحات الأكاديميات" })).not.toBeInTheDocument();
  expect(screen.queryByText("0 أكاديمية · صفحة 1")).not.toBeInTheDocument();
  const link = screen.queryByRole("link", { name: "تسجيل الدخول في نافذة أخرى" });
  if (login) expect(link).toHaveAttribute("href", "/platform/login");
  else expect(link).not.toBeInTheDocument();
});
