import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { PrivateCorrectionAction } from "./private-correction-action";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const quote = { month: "2026-11-01", quoteHash: "c".repeat(64), canCorrect: true, message: "الكشف السابق محفوظ؛ تصحيح العمولة فقط.", difference: -20, currency: "EGP" };
const response = (body: unknown, status = 200) => Promise.resolve(new Response(JSON.stringify(body), { status }));
function mock(canCorrect = true, status = 201) {
  global.fetch = vi.fn((url, init) => String(url).endsWith("csrf") ? response({ token: "csrf" }) : init?.method === "POST" ? response(status === 201 ? { id: "correction" } : { title: "تغيرت المراجعة؛ أعد المعاينة." }, status) : response({ ...quote, canCorrect })) as typeof fetch;
}
it("requires preview and an explicit month confirmation without an editable amount; sends source quote once", async () => {
  mock(); const changed = vi.fn(async () => {});
  render(<PrivateCorrectionAction path="/source" onChanged={changed} />);
  expect(fetch).not.toHaveBeenCalled(); expect(screen.queryByRole("spinbutton")).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "معاينة شهر التصحيح" }));
  await screen.findByText(quote.message);
  expect(screen.getByLabelText("شهر تصحيح العمولة")).toHaveValue("2026-11");
  expect(vi.mocked(fetch).mock.calls.some(([, init]) => init?.method === "POST")).toBe(false);
  fireEvent.click(screen.getByRole("button", { name: "تأكيد تصحيح العمولة للشهر المختار" }));
  await waitFor(() => expect(changed).toHaveBeenCalledOnce());
  const request = vi.mocked(fetch).mock.calls.find(([, init]) => init?.method === "POST")!;
  expect(request[0]).toBe("/source/corrections");
  expect(JSON.parse(String(request[1]?.body))).toEqual({ month: quote.month, quoteHash: quote.quoteHash, note: null });
  expect((request[1]?.headers as Record<string, string>)["Idempotency-Key"]).toHaveLength(36);
});
it("invalidates the quote when month changes and never confirms a closed month", async () => {
  mock(); render(<PrivateCorrectionAction path="/source" onChanged={async () => {}} />);
  fireEvent.click(screen.getByRole("button", { name: "معاينة شهر التصحيح" })); await screen.findByText(quote.message);
  fireEvent.change(screen.getByLabelText("شهر تصحيح العمولة"), { target: { value: "2026-12" } });
  expect(screen.queryByRole("button", { name: "تأكيد تصحيح العمولة للشهر المختار" })).not.toBeInTheDocument();
  mock(false); fireEvent.click(screen.getByRole("button", { name: "معاينة شهر التصحيح" })); await screen.findByText(quote.message);
  expect(screen.queryByRole("button", { name: "تأكيد تصحيح العمولة للشهر المختار" })).not.toBeInTheDocument();
  expect(String(vi.mocked(fetch).mock.calls[0][0])).toContain("?month=2026-12-01");
});
it("shows a positive source delta without letting the reviewer invent an amount",async()=>{
  global.fetch=vi.fn((url,init)=>String(url).endsWith("csrf")?response({token:"csrf"}):init?.method==="POST"?response({id:"positive"},201):response({...quote,difference:50})) as typeof fetch;
  const changed=vi.fn(async()=>{});render(<PrivateCorrectionAction path="/source" onChanged={changed}/>);fireEvent.click(screen.getByRole("button",{name:"معاينة شهر التصحيح"}));expect(await screen.findByText(/فرق العمولة: 50/)).toBeVisible();expect(screen.queryByRole("spinbutton")).not.toBeInTheDocument();fireEvent.click(screen.getByRole("button",{name:"تأكيد تصحيح العمولة للشهر المختار"}));await waitFor(()=>expect(changed).toHaveBeenCalledOnce());const call=vi.mocked(fetch).mock.calls.find(([,o])=>o?.method==="POST");expect(JSON.parse(String(call?.[1]?.body))).toEqual({month:quote.month,quoteHash:quote.quoteHash,note:null});
});
it("keeps stale denial visible, does not claim success and reuses the key on unchanged retry", async () => {
  mock(true, 409); const changed = vi.fn(async () => {});
  render(<PrivateCorrectionAction path="/source" onChanged={changed} />);
  fireEvent.click(screen.getByRole("button", { name: "معاينة شهر التصحيح" })); await screen.findByText(quote.message);
  fireEvent.click(screen.getByRole("button", { name: "تأكيد تصحيح العمولة للشهر المختار" })); await screen.findByRole("alert");
  fireEvent.click(screen.getByRole("button", { name: "تأكيد تصحيح العمولة للشهر المختار" })); await waitFor(() => expect(vi.mocked(fetch).mock.calls.filter(([, i]) => i?.method === "POST")).toHaveLength(2));
  const posts = vi.mocked(fetch).mock.calls.filter(([, i]) => i?.method === "POST");
  expect(posts[0][1]?.headers).toEqual(posts[1][1]?.headers); expect(changed).not.toHaveBeenCalled();
});
