import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { ContractHistory } from "./contract-history";
const initial = { name: "موظف عقد اصطناعي", currentMonth: "2026-10-01", contractStart: "2000-01-01", contractEnd: null, hasVerifiedHistory: true, revision: 1, canChange: true, items: [] };
const response = (value: unknown, status = 200) => Promise.resolve(new Response(JSON.stringify(value), { status }));
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
it("preloads the last scheduled future contract rather than accidentally replacing it with current terms",async()=>{
  global.fetch=vi.fn(()=>response({...initial,revision:2,items:[{id:"v2",revision:2,effectiveFrom:"2026-11-01",contractStart:"2000-01-01",contractEnd:"2026-11-15"}]})) as typeof fetch;
  render(<ContractHistory id="worker"/>); await screen.findByRole("heading",{name:"تغيير مؤرخ للعقد"}); expect(screen.getByLabelText("شهر سريان العقد")).toHaveValue("2026-11"); expect(screen.getByLabelText("عقد محدد المدة")).toBeChecked(); expect(screen.getByLabelText("تاريخ نهاية العقد")).toHaveValue("2026-11-15");
});
it("reviews a month-specific contract, confirms once with revision and optional note, then refreshes", async () => {
  global.fetch = vi.fn((url, init) => String(url).includes("csrf") ? response({ token: "csrf" }) : init?.method === "POST" ? response({ id: "v2" }, 201) : response(initial)) as typeof fetch;
  render(<ContractHistory id="worker" />); await screen.findByRole("heading", { name: "تغيير مؤرخ للعقد" });
  fireEvent.change(screen.getByLabelText("شهر سريان العقد"), { target: { value: "2026-11" } });
  fireEvent.click(screen.getByLabelText("عقد محدد المدة")); fireEvent.change(screen.getByLabelText("تاريخ نهاية العقد"), { target: { value: "2026-11-15" } });
  expect(screen.getByLabelText("ملاحظة العقد — اختياري")).not.toBeRequired(); fireEvent.click(screen.getByRole("button", { name: "مراجعة تغيير العقد" }));
  expect(screen.getByRole("region", { name: "تأكيد العقد المؤرخ" })).toHaveTextContent("النسخة السابقة لا تُحذف"); expect(vi.mocked(fetch).mock.calls.some(([, i]) => i?.method === "POST")).toBe(false);
  fireEvent.click(screen.getByRole("button", { name: "تأكيد اعتماد العقد" })); await screen.findByRole("status");
  const call = vi.mocked(fetch).mock.calls.find(([, i]) => i?.method === "POST")!;
  expect(JSON.parse(String(call[1]?.body))).toEqual({ effectiveFrom: "2026-11-01", contractStart: "2000-01-01", contractEnd: "2026-11-15", expectedRevision: 1, note: null }); expect((call[1]?.headers as Record<string, string>)["Idempotency-Key"]).toHaveLength(36);
});
it("cannot save with read-only permission and marks missing history instead of pretending it is verified", async () => {
  global.fetch = vi.fn(() => response({ ...initial, canChange: false, hasVerifiedHistory: false })) as typeof fetch;
  render(<ContractHistory id="worker" />); await screen.findByText("مرجع قديم غير مؤرخ؛ لا نفترض تاريخًا سابقًا موثقًا."); expect(screen.queryByRole("button", { name: "مراجعة تغيير العقد" })).not.toBeInTheDocument();
});
it("preserves stale denial and idempotency on unchanged retry without claiming success", async () => {
  global.fetch = vi.fn((url, init) => String(url).includes("csrf") ? response({ token: "csrf" }) : init?.method === "POST" ? response({ title: "تغير السجل؛ أعد المراجعة." }, 409) : response(initial)) as typeof fetch;
  render(<ContractHistory id="worker" />); fireEvent.click(await screen.findByRole("button", { name: "مراجعة تغيير العقد" })); fireEvent.click(screen.getByRole("button", { name: "تأكيد اعتماد العقد" })); await screen.findByRole("alert");
  expect(screen.queryByRole("status")).not.toBeInTheDocument(); fireEvent.click(screen.getByRole("button", { name: "تأكيد اعتماد العقد" })); await waitFor(() => expect(vi.mocked(fetch).mock.calls.filter(([, i]) => i?.method === "POST")).toHaveLength(2));
  const calls = vi.mocked(fetch).mock.calls.filter(([, i]) => i?.method === "POST"); expect(calls[0][1]?.headers).toEqual(calls[1][1]?.headers);
});
