import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { UnassignedEnrollment } from "./unassigned-enrollment";
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
it("adds a sport without a group or payment and refreshes only after server confirmation", async () => {
  const onCreated = vi.fn(); const requests: { url: string; body: unknown }[] = [];
  vi.stubGlobal("fetch", vi.fn(async (url: string, init?: RequestInit) => {
    if (url.endsWith("options")) return Response.json({ branches: [{ id: "b", arabicName: "فرع تجريبي" }], sports: [{ id: "s", arabicName: "سباحة" }] });
    if (url.endsWith("csrf")) return Response.json({ token: "token" });
    requests.push({ url, body: JSON.parse(String(init?.body)) }); return Response.json({ id: "e", placement: "Unassigned", subscriptionCreated: false }, { status: 201 });
  }));
  render(<UnassignedEnrollment playerId="p" onCreated={onCreated} />);
  await screen.findByRole("option", { name: "سباحة" });
  fireEvent.change(screen.getByLabelText("رياضة التسجيل"), { target: { value: "s" } }); fireEvent.change(screen.getByLabelText("فرع التسجيل"), { target: { value: "b" } });
  fireEvent.click(screen.getByRole("button", { name: "حفظ في انتظار التسكين" })); await waitFor(() => expect(onCreated).toHaveBeenCalledOnce());
  expect(requests).toEqual([{ url: "/api/v1/people/players/p/unassigned-enrollments", body: { sportId: "s", branchId: "b" } }]);
});
it("does not signal saved when the tenant or branch denies the request", async () => {
  const onCreated = vi.fn(); vi.stubGlobal("fetch", vi.fn(async (url: string) => url.endsWith("options") ? Response.json({ branches: [], sports: [] }) : url.endsWith("csrf") ? Response.json({ token: "t" }) : Response.json({ title: "الفرع غير متاح" }, { status: 403 })));
  render(<UnassignedEnrollment playerId="p" onCreated={onCreated} />); await waitFor(() => expect(screen.getByRole("button")).toBeEnabled());
  fireEvent.submit(screen.getByRole("button").closest("form")!); expect(await screen.findByRole("alert")).toHaveTextContent("الفرع غير متاح"); expect(onCreated).not.toHaveBeenCalled();
});
