import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { SportOperationsSettings } from "./sport-operations-settings";
afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); });
const base = { facilitiesEnabled: true, sports: [{ id: "football", name: "كرة القدم", kind: "Football", isActive: true, version: 1 }, { id: "swim", name: "السباحة", kind: "Swimming", isActive: true, version: 2 }], branches: [{ id: "a", name: "فرع أ" }, { id: "b", name: "فرع ب" }] };
function setup() {
  const fetcher = vi.fn(async (url: string, init?: RequestInit) => ({ ok: true, status: 200, json: async () => url.endsWith("/csrf") ? { token: "token" } : url.endsWith("/swimming") ? { policies: [{ branchId: "a", enabled: true, privateTrainingEnabled: false, openSwimEnabled: false, dayPassEnabled: true, version: 10 }], history: [] } : init?.method === "PUT" ? {} : base }));
  vi.stubGlobal("fetch", fetcher); render(<SportOperationsSettings />); return fetcher;
}
it("does not show or request swimming settings in a football context", async () => {
  const fetcher = setup(); fireEvent.change(await screen.findByLabelText("الرياضة"), { target: { value: "football" } });
  expect(screen.queryByRole("region", { name: "إعدادات السباحة" })).not.toBeInTheDocument();
  expect(fetcher.mock.calls.some(([url]) => url.includes("/swimming"))).toBe(false);
  expect(screen.getByLabelText("نوع الرياضة")).toHaveValue("Football");
});
it("saves only the selected branch with its state version and preserves the other branch", async () => {
  const fetcher = setup(); fireEvent.change(await screen.findByLabelText("الرياضة"), { target: { value: "swim" } });
  const heading = await screen.findByRole("heading", { name: "فرع أ" }); const form = heading.closest("form")!;
  fireEvent.click(within(form).getByLabelText("تشغيل مرافق السباحة في هذا الفرع"));
  fireEvent.click(within(form).getByRole("button", { name: "حفظ إعدادات الفرع" }));
  await waitFor(() => expect(fetcher.mock.calls.some(([url, init]) => url.endsWith("/swimming/a") && init?.method === "PUT")).toBe(true));
  const sent = fetcher.mock.calls.find(([url, init]) => url.endsWith("/swimming/a") && init?.method === "PUT")!;
  expect(JSON.parse(sent[1]!.body as string)).toEqual({ enabled: false, privateTrainingEnabled: false, openSwimEnabled: false, dayPassEnabled: true, version: 10 });
  expect(fetcher.mock.calls.some(([url, init]) => url.endsWith("/swimming/b") && init?.method === "PUT")).toBe(false);
  expect(await screen.findByRole("status")).toHaveTextContent("تم حفظ إعدادات فرع أ");
});
it("shows the Arabic server failure without offering a working settings form", async () => {
  global.fetch = vi.fn().mockResolvedValue({ ok: false, status: 403 }); render(<SportOperationsSettings />);
  expect(await screen.findByText(/خصائص الرياضات متاحة للمالك فقط/)).toBeVisible();
  expect(screen.queryByLabelText("الرياضة")).not.toBeInTheDocument();
});
