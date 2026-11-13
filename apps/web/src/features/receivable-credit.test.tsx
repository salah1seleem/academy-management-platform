import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { webcrypto } from "node:crypto";
import { ReceivableCredit } from "./receivable-credit";
let search = new URLSearchParams();
vi.mock("next/navigation", () => ({ useSearchParams: () => search }));
const reply = (v: unknown, status = 200) => new Response(JSON.stringify(v), { status });
const options = { source: { id: "source", player: "زياد اصطناعي", description: "مصدر موثق", branch: "فرع", currency: "EGP", outstanding: "-100.00" }, targets: [{ id: "target", description: "مطالبة مستفيدة", branch: "فرع", currency: "EGP", outstanding: "300.00" }] };
const quote = { quoteHash: "A".repeat(64), player: "زياد اصطناعي", currency: "EGP", amount: "60.00", reason: "تخصيص موثق", source: { id: "source", description: "مصدر موثق", before: "-100.00", after: "-40.00" }, target: { id: "target", description: "مطالبة مستفيدة", before: "300.00", after: "240.00" } };
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
beforeEach(() => { search = new URLSearchParams(); sessionStorage.clear(); vi.stubGlobal("crypto", webcrypto); });
async function review() { render(<ReceivableCredit sourceId="source" />); await screen.findByRole("option", { name: /مطالبة مستفيدة/ }); fireEvent.change(screen.getByLabelText("المطالبة المستفيدة"), { target: { value: "target" } }); fireEvent.change(screen.getByLabelText("المبلغ المراد استخدامه"), { target: { value: "60.00" } }); fireEvent.change(screen.getByLabelText("سبب التخصيص"), { target: { value: "تخصيص موثق" } }); fireEvent.click(screen.getByRole("button", { name: "مراجعة استخدام الرصيد" })); await screen.findByRole("region", { name: "مراجعة الرصيد الدائن" }); }
it("verifies the original approval key after a lost reply and reload without writing again", async () => {
  const keys: string[] = [];
  let lookups = 0;
  global.fetch = vi.fn(async (url, init) => {
    const path = String(url);
    if (path.endsWith("csrf")) return reply({ token: "csrf" });
    if (path.endsWith("options")) return reply(options);
    if (path.endsWith("quote")) return reply(quote);
    if (path.endsWith("/attempt")) {
      expect((init?.headers as Record<string, string>)["Idempotency-Key"]).toBe(keys[0]);
      return ++lookups === 1 ? reply({}, 503) : reply({
        id: "application", sourceDocumentId: "source", targetDocumentId: "target", targetBranchId: "actual-branch",
        amount: "60.00", currency: "EGP", sourceRemaining: "40.00", targetRemaining: "240.00",
      });
    }
    expect(init?.method).toBe("POST");
    keys.push((init?.headers as Record<string, string>)["Idempotency-Key"]);
    expect(JSON.parse(init?.body as string)).toMatchObject({ amount: "60.00", targetDocumentId: "target" });
    throw new Error("technical network error");
  });
  await review();
  expect(keys).toHaveLength(0);
  expect(screen.getByRole("button", { name: "اعتماد استخدام الرصيد" })).toBeDisabled();
  fireEvent.click(screen.getByRole("checkbox", { name: "أعتمد استخدام الرصيد دون قبض جديد" }));
  fireEvent.click(screen.getByRole("button", { name: "اعتماد استخدام الرصيد" }));
  await screen.findByText("تعذر التحقق من نتيجة التسوية.");
  expect(screen.queryByText("technical network error")).not.toBeInTheDocument();
  expect(screen.queryByLabelText("المطالبة المستفيدة")).not.toBeInTheDocument();
  cleanup();
  render(<ReceivableCredit sourceId="source" />);
  await screen.findByRole("heading", { name: "تم استخدام الرصيد الدائن دون قبض جديد" });
  expect(keys).toHaveLength(1);
  expect(screen.getByRole("link", { name: "عرض المطالبة المستفيدة" })).toHaveAttribute("href", "/dashboard/finance/receivables/target?branchId=actual-branch");
});

it("a definite stale-quote conflict unlocks inputs for fresh review", async () => {
  let reads = 0;
  global.fetch = vi.fn(async url => String(url).endsWith("csrf") ? reply({ token: "csrf" }) : String(url).endsWith("options")
    ? reply(++reads === 1 ? options : { ...options, source: { ...options.source, outstanding: "-20.00" } })
    : String(url).endsWith("quote") ? reply(quote) : reply({ title: "تغير الرصيد. راجع القيم الجديدة قبل الاعتماد." }, 409));
  await review(); fireEvent.click(screen.getByRole("checkbox")); fireEvent.click(screen.getByRole("button", { name: "اعتماد استخدام الرصيد" })); await screen.findByText("تغير الرصيد. راجع القيم الجديدة قبل الاعتماد."); await waitFor(() => expect(screen.getByLabelText("المبلغ المراد استخدامه")).toBeEnabled()); expect(screen.queryByRole("region", { name: "مراجعة الرصيد الدائن" })).not.toBeInTheDocument();
  expect(screen.getByLabelText("المبلغ المراد استخدامه")).toHaveAttribute("max", "20");
  expect(screen.getByRole("button", { name: "مراجعة استخدام الرصيد" })).toBeDisabled();
  expect(reads).toBe(2);
});
it("fails closed for unauthorized sources", async () => {
  global.fetch = vi.fn(async () => reply({}, 403)); render(<ReceivableCredit sourceId="foreign" />); await screen.findByText(/استخدام الرصيد الدائن متاح للمالك/); expect(screen.queryByLabelText("المبلغ المراد استخدامه")).not.toBeInTheDocument();
});

it("retries a temporary read failure and shows an exhausted balance without offering another action", async () => {
  const read = vi.fn(async () => reply({}, read.mock.calls.length === 1 ? 503 : 409));
  vi.stubGlobal("fetch", read);
  render(<ReceivableCredit sourceId="source" />);
  await screen.findByText("تعذر تحميل بيانات الرصيد الدائن.");
  fireEvent.click(screen.getByRole("button", { name: "إعادة المحاولة" }));
  await screen.findByText("لا يوجد رصيد دائن متاح في هذه المطالبة.");
  expect(screen.queryByRole("button", { name: "إعادة المحاولة" })).not.toBeInTheDocument();
  expect(screen.queryByLabelText("المطالبة المستفيدة")).not.toBeInTheDocument();
  expect(screen.getByRole("link", { name: "العودة إلى المطالبة الأصلية" })).toBeVisible();
});

it("shows the available credit and caps review at the selected claim balance", async () => {
  search = new URLSearchParams("state=credit&branchId=branch&unrelated=1");
  global.fetch = vi.fn(async () => reply({ ...options, targets: [{ ...options.targets[0], outstanding: "20.00" }] }));
  render(<ReceivableCredit sourceId="source" />);
  expect(await screen.findByText("100 جنيه")).toBeVisible();
  expect(screen.getByRole("link", { name: "العودة إلى المطالبة الأصلية" })).toHaveAttribute("href", "/dashboard/finance/receivables/source?state=credit&branchId=branch");
  expect(screen.getByLabelText("المبلغ المراد استخدامه")).toBeDisabled();
  fireEvent.change(screen.getByLabelText("المطالبة المستفيدة"), { target: { value: "target" } });
  const input = screen.getByLabelText("المبلغ المراد استخدامه");
  expect(input).toHaveAttribute("max", "20");
  expect(input).toHaveAccessibleDescription("أقصى مبلغ لهذه المطالبة 20 جنيه");
  fireEvent.change(input, { target: { value: "20.01" } });
  fireEvent.change(screen.getByLabelText("سبب التخصيص"), { target: { value: "سبب تجريبي" } });
  expect(screen.getByRole("button", { name: "مراجعة استخدام الرصيد" })).toBeDisabled();
  fireEvent.change(input, { target: { value: "20.00" } });
  expect(screen.getByRole("button", { name: "مراجعة استخدام الرصيد" })).toBeEnabled();
});

it("clears the previous document on navigation and ignores its late options response", async () => {
  let finish: (value: Response) => void = () => {};
  global.fetch = vi.fn(async url => String(url).includes("/source/")
    ? new Promise<Response>(resolve => { finish = resolve; })
    : reply({ ...options, source: { ...options.source, id: "new", player: "لاعبة تجريبية" }, targets: [] }));
  const view = render(<ReceivableCredit sourceId="source" />);
  view.rerender(<ReceivableCredit sourceId="new" />);
  expect(await screen.findByRole("heading", { name: "لاعبة تجريبية" })).toBeVisible();
  await act(async () => finish(reply(options)));
  expect(screen.queryByText("زياد اصطناعي")).toBeNull();
  expect(screen.getByText("لا توجد مطالبة قائمة لنفس اللاعب والعملة.")).toBeVisible();
  expect(screen.queryByLabelText("المبلغ المراد استخدامه")).toBeNull();
});

it("rejects a review for a different claim before showing the approval action", async () => {
  global.fetch = vi.fn(async url => String(url).endsWith("csrf") ? reply({ token: "csrf" })
    : String(url).endsWith("options") ? reply(options) : reply({ ...quote, target: { ...quote.target, id: "another" } }));
  render(<ReceivableCredit sourceId="source" />);
  await screen.findByRole("option", { name: /مطالبة مستفيدة/ });
  fireEvent.change(screen.getByLabelText("المطالبة المستفيدة"), { target: { value: "target" } });
  fireEvent.change(screen.getByLabelText("المبلغ المراد استخدامه"), { target: { value: "60" } });
  fireEvent.change(screen.getByLabelText("سبب التخصيص"), { target: { value: quote.reason } });
  fireEvent.click(screen.getByRole("button", { name: "مراجعة استخدام الرصيد" }));
  await screen.findByText("تعذر تأكيد مراجعة الرصيد. أعد المحاولة.");
  expect(screen.queryByRole("button", { name: "اعتماد استخدام الرصيد" })).toBeNull();
});
