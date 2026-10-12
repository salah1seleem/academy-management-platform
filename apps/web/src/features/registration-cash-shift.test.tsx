import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { RegistrationCashShift } from "./registration-cash-shift";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const props = { branchId: "branch", planId: "plan", value: "", onChange: vi.fn() };
it("loads authorized shifts without automatic selection and reports the selected id", async () => {
  const changed = vi.fn(); global.fetch = vi.fn(async () => Response.json({ sessions: [{ id: "shift", name: "صندوق الفرع", branch: "فرع اصطناعي" }] }));
  render(<RegistrationCashShift {...props} onChange={changed} />);
  const select = await screen.findByRole("combobox", { name: "وردية التحصيل" }); expect(select).toHaveValue("");
  expect(fetch).toHaveBeenCalledWith("/api/v1/people/registration-cash-options?branchId=branch&planId=plan", { cache: "no-store" });
  fireEvent.change(select, { target: { value: "shift" } }); expect(changed).toHaveBeenLastCalledWith("shift");
});
it("shows setup guidance for an empty authorized list", async () => {
  global.fetch = vi.fn(async () => Response.json({ sessions: [] })); render(<RegistrationCashShift {...props} />);
  await screen.findByText("لا توجد وردية مفتوحة لك في هذا الفرع وبعملة الباقة.");
  expect(screen.queryByRole("option", { name: /صندوق/ })).not.toBeInTheDocument();
  expect(screen.getByRole("link", { name: "فتح الصناديق والورديات" })).toHaveAttribute("href", "/dashboard/finance/cash-registers");
});
it("keeps server permission or disabled errors in Arabic without a usable shift", async () => {
  global.fetch = vi.fn(async () => Response.json({ title: "النقدي غير مفعل." }, { status: 409 })); render(<RegistrationCashShift {...props} />);
  expect(await screen.findByRole("alert")).toHaveTextContent("النقدي غير مفعل."); expect(screen.queryByRole("combobox")).not.toBeInTheDocument();
});
it("refresh clears the selected shift immediately and never restores a stale context response", async () => {
  let finish: (response: Response) => void = () => {}; const changed = vi.fn();
  global.fetch = vi.fn(url => String(url).includes("branchId=old") ? new Promise<Response>(resolve => { finish = resolve; }) : Promise.resolve(Response.json({ sessions: [{ id: "new", name: "صندوق حالي", branch: "فرع حالي" }] })));
  const view = render(<RegistrationCashShift {...props} branchId="old" onChange={changed} />);
  view.rerender(<RegistrationCashShift {...props} branchId="current" value="new" onChange={changed} />);
  await screen.findByRole("option", { name: /صندوق حالي/ }); finish(Response.json({ sessions: [{ id: "old", name: "صندوق قديم", branch: "قديم" }] }));
  await waitFor(() => expect(screen.queryByRole("option", { name: /صندوق قديم/ })).not.toBeInTheDocument());
  changed.mockClear(); fireEvent.click(screen.getByRole("button", { name: "تحديث الورديات" })); expect(changed).toHaveBeenCalledWith("");
});
it("does not fetch until branch and plan are supplied", () => {
  global.fetch = vi.fn(); render(<RegistrationCashShift {...props} planId="" />);
  expect(fetch).not.toHaveBeenCalled(); expect(screen.getByText("اختر الفرع والباقة لعرض وردياتك المتاحة.")).toBeVisible();
});
it("never exposes a technical network failure to the user", async () => {
  global.fetch = vi.fn(async () => { throw new TypeError("Failed to fetch"); }); render(<RegistrationCashShift {...props} />);
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل الورديات."); expect(screen.queryByText(/Failed to fetch/)).not.toBeInTheDocument();
  expect(screen.getByRole("button", { name: "تحديث الورديات" })).toBeEnabled();
});
