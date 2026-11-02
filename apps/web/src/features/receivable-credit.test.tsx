import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { webcrypto } from "node:crypto";
import { ReceivableCredit } from "./receivable-credit";
const reply = (v: unknown, status = 200) => new Response(JSON.stringify(v), { status });
const options = { source: { id: "source", player: "زياد اصطناعي", description: "مصدر موثق", branch: "فرع", currency: "EGP", outstanding: "-100.00" }, targets: [{ id: "target", description: "مطالبة مستفيدة", branch: "فرع", currency: "EGP", outstanding: "300.00" }] };
const quote = { quoteHash: "A".repeat(64), player: "زياد اصطناعي", currency: "EGP", amount: "60.00", reason: "تخصيص موثق", source: { id: "source", description: "مصدر موثق", before: "-100.00", after: "-40.00" }, target: { id: "target", description: "مطالبة مستفيدة", before: "300.00", after: "240.00" } };
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
beforeEach(() => { sessionStorage.clear(); vi.stubGlobal("crypto", webcrypto); });
async function review() { render(<ReceivableCredit sourceId="source" />); await screen.findByRole("option", { name: /مطالبة مستفيدة/ }); fireEvent.change(screen.getByLabelText("المطالبة المستفيدة"), { target: { value: "target" } }); fireEvent.change(screen.getByLabelText("المبلغ المراد استخدامه"), { target: { value: "60.00" } }); fireEvent.change(screen.getByLabelText("سبب التخصيص"), { target: { value: "تخصيص موثق" } }); fireEvent.click(screen.getByRole("button", { name: "مراجعة استخدام الرصيد" })); await screen.findByRole("region", { name: "مراجعة الرصيد الدائن" }); }
it("reviews without saving, requires explicit consent and retries the exact financial intent", async () => {
  let attempts = 0; const keys: string[] = [], bodies: string[] = [];
  global.fetch = vi.fn(async (url, init) => {
    const path = String(url); if (path.endsWith("csrf")) return reply({ token: "csrf" }); if (path.endsWith("options")) return reply(options); if (path.endsWith("quote")) return reply(quote);
    keys.push((init?.headers as Record<string, string>)["Idempotency-Key"]); bodies.push(init?.body as string); if (++attempts === 1) throw new Error("technical network error"); return reply({ id: "application" });
  });
  await review(); expect(keys).toHaveLength(0); expect(screen.getByRole("button", { name: "اعتماد استخدام الرصيد" })).toBeDisabled();
  fireEvent.click(screen.getByRole("checkbox", { name: "أعتمد استخدام الرصيد دون قبض جديد" })); fireEvent.click(screen.getByRole("button", { name: "اعتماد استخدام الرصيد" })); await screen.findByText(/تعذر تأكيد نتيجة التخصيص/);
  expect(screen.queryByText("technical network error")).not.toBeInTheDocument(); expect(screen.getByLabelText("المطالبة المستفيدة")).toBeDisabled(); fireEvent.click(screen.getByRole("button", { name: "إعادة تأكيد نفس التخصيص" })); await screen.findByRole("heading", { name: "تم استخدام الرصيد الدائن دون قبض جديد" }); expect(keys[0]).toBe(keys[1]); expect(bodies[0]).toBe(bodies[1]); expect(JSON.parse(bodies[0])).toMatchObject({ amount: "60.00", targetDocumentId: "target" }); expect(screen.getByRole("link", { name: "عرض المطالبة المستفيدة" })).toHaveAttribute("href", "/dashboard/finance/receivables/target");
});
it("a definite stale-quote conflict unlocks inputs for fresh review", async () => {
  global.fetch = vi.fn(async url => String(url).endsWith("csrf") ? reply({ token: "csrf" }) : String(url).endsWith("options") ? reply(options) : String(url).endsWith("quote") ? reply(quote) : reply({ title: "تغير الرصيد. راجع القيم الجديدة قبل الاعتماد." }, 409));
  await review(); fireEvent.click(screen.getByRole("checkbox")); fireEvent.click(screen.getByRole("button", { name: "اعتماد استخدام الرصيد" })); await screen.findByText("تغير الرصيد. راجع القيم الجديدة قبل الاعتماد."); await waitFor(() => expect(screen.getByLabelText("المبلغ المراد استخدامه")).toBeEnabled()); expect(screen.queryByRole("region", { name: "مراجعة الرصيد الدائن" })).not.toBeInTheDocument();
});
it("fails closed for unauthorized sources", async () => {
  global.fetch = vi.fn(async () => reply({}, 403)); render(<ReceivableCredit sourceId="foreign" />); await screen.findByText(/استخدام الرصيد الدائن متاح للمالك/); expect(screen.queryByLabelText("المبلغ المراد استخدامه")).not.toBeInTheDocument();
});
