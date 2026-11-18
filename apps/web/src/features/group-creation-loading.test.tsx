import { act, cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { GroupCreation } from "./group-creation";

vi.mock("next/navigation", () => ({ useRouter: () => ({ push: vi.fn() }) }));
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
const options = {
  branches: [{ id: "b", arabicName: "فرع اختباري" }], sports: [{ id: "s", arabicName: "سباحة اختبارية" }],
  categories: [{ id: "a", arabicName: "فئة اختبارية" }], coaches: [{ id: "c", arabicName: "مدرب اختباري" }],
};
const levels = [{ id: "l", arabicName: "مستوى اختباري", sportId: "s" }];
const facilities = { spaces: [{ id: "space", sportId: "s", name: "مسبح اختباري" }], resources: [{
  id: "r", name: "حارة اختبارية", facilitySpaceId: "space", branchId: "b", capacity: 20,
  approvedAtUtc: "2026-11-18T00:00:00Z", serviceKinds: ["Training"],
}] };
const message = "تعذر تحميل خيارات إنشاء المجموعة. أعد المحاولة.";
const failures = [
  { name: "network", path: "/structure/options", fail: () => Promise.reject(new TypeError("Failed to fetch")) },
  { name: "service unavailable", path: "/structure/options", fail: () => new Response("Synthetic upstream failure", { status: 503 }) },
  { name: "invalid JSON", path: "/structure/options", fail: () => new Response("{invalid-json") },
  { name: "missing option arrays", path: "/structure/options", fail: () => Response.json({ branches: [] }) },
  { name: "malformed levels", path: "/training-levels", fail: () => Response.json([{}]) },
  { name: "malformed permissions", path: "/my-permissions", fail: () => Response.json({ includes: true }) },
  { name: "facility read failure", path: "/facilities", fail: () => new Response(null, { status: 503 }) },
  { name: "malformed facilities", path: "/facilities", fail: () => Response.json({ spaces: [], resources: [{}] }) },
];
it.each(failures)("recovers $name in Arabic without a partial form or a write", async failure => {
  let broken = true;
  const fetcher = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    expect(init?.method ?? "GET").toBe("GET");
    const path = String(input);
    if (broken && path.endsWith(failure.path)) return failure.fail();
    return Response.json(path.endsWith("/options") ? options : path.endsWith("/training-levels") ? levels : path.endsWith("/my-permissions") ? ["facilities.read"] : facilities);
  });
  vi.stubGlobal("fetch", fetcher);
  render(<GroupCreation />);
  expect(await screen.findByRole("alert")).toHaveTextContent(message);
  expect(screen.queryByRole("textbox", { name: "اسم المجموعة" })).not.toBeInTheDocument();
  expect(screen.queryByText(/Failed to fetch|invalid-json|Synthetic upstream/)).not.toBeInTheDocument();
  const count = fetcher.mock.calls.length;
  broken = false;
  fireEvent.click(screen.getByRole("button", { name: "إعادة تحميل خيارات المجموعة" }));
  expect(await screen.findByRole("textbox", { name: "اسم المجموعة" })).toBeEnabled();
  expect(fetcher.mock.calls.length).toBeGreaterThan(count);
  expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  expect(screen.getByRole("option", { name: "فرع اختباري" })).toBeInTheDocument();
});

it("waits for permitted facility data before enabling the draft", async () => {
  let finish!: (response: Response) => void;
  const delayed = new Promise<Response>(resolve => { finish = resolve; });
  const fetcher = vi.fn(async (input: RequestInfo | URL) => {
    const path = String(input);
    if (path.endsWith("/facilities")) return delayed;
    return Response.json(path.endsWith("/options") ? options : path.endsWith("/training-levels") ? levels : ["facilities.read"]);
  });
  vi.stubGlobal("fetch", fetcher);
  render(<GroupCreation />);
  await act(async () => { await Promise.resolve(); });
  expect(fetcher.mock.calls.some(([path]) => String(path).endsWith("/facilities"))).toBe(true);
  expect(screen.queryByRole("textbox", { name: "اسم المجموعة" })).not.toBeInTheDocument();
  await act(async () => { finish(Response.json(facilities)); });
  expect(await screen.findByRole("textbox", { name: "اسم المجموعة" })).toBeEnabled();
});

it("does not request facilities without the read permission", async () => {
  const fetcher = vi.fn(async (input: RequestInfo | URL) => {
    const path = String(input);
    return Response.json(path.endsWith("/options") ? options : path.endsWith("/training-levels") ? levels : []);
  });
  vi.stubGlobal("fetch", fetcher);
  render(<GroupCreation />);
  expect(await screen.findByRole("textbox", { name: "اسم المجموعة" })).toBeEnabled();
  expect(fetcher.mock.calls.some(([path]) => String(path).endsWith("/facilities"))).toBe(false);
});
