import { act, cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { GuardianReceipts } from "./guardian-receipts";
import { ReceiptPrint } from "./receipt-print";

const router = vi.hoisted(() => ({ push: vi.fn() }));
vi.mock("next/navigation", () => ({ useRouter: () => router }));

afterEach(() => { cleanup(); vi.unstubAllGlobals(); router.push.mockClear(); });

const children = [
  { id: "71a7c4c8-3338-4908-bb0b-306af79a9b44", arabicName: "آدم العرض" },
  { id: "f4f7af67-52f9-410b-a693-a9e136225782", arabicName: "سلمى العرض" },
];
const row = {
  id: "c2f30e05-a63a-4b57-a1a1-6979e355b9fb", player: "اسم اللاعب السابق",
  currentPlayer: children[0].arabicName,
  receiptNumber: "RC-DEMO-012", sport: "كرة القدم", plan: "اشتراك شهري", amount: 800,
  finalAmount: 760, currency: "EGP", paidAtUtc: "2026-11-07T10:30:00Z", isVoided: true,
};
const feed = (items: unknown[], total = 55) => Response.json({ items, children, total, pageSize: 25 });

it("keeps the selected child and page in receipt links and marks reversals beside the stored amount", async () => {
  const tournament = { ...row, id: "tournament", receiptNumber: "TR-001", source: "Tournament", isVoided: false };
  const fetcher = vi.fn(async () => feed([row, tournament]));
  vi.stubGlobal("fetch", fetcher);
  render(<GuardianReceipts playerId={children[0].id} page={2} />);
  const receipt = await screen.findByRole("link", { name: /RC-DEMO-012/ });
  expect(receipt).toHaveAttribute("href", `/guardian/receipts/${row.id}?playerId=${children[0].id}&page=2`);
  expect(within(receipt).getByText("760 جنيه")).toBeVisible();
  expect(within(receipt).getByText(children[0].arabicName)).toBeVisible();
  expect(within(receipt).getByText("الاسم وقت الدفع: اسم اللاعب السابق")).toBeVisible();
  expect(within(receipt).getByText("إيصال معكوس")).toBeVisible();
  expect(screen.getByRole("link", { name: /TR-001/ })).toHaveAttribute("href", `/guardian/receipts/tournament?source=Tournament&playerId=${children[0].id}&page=2`);
  const filter = screen.getByRole("combobox", { name: "اللاعب" });
  expect(filter).toHaveValue(children[0].id);
  fireEvent.change(filter, { target: { value: children[1].id } });
  expect(router.push).toHaveBeenCalledWith(`/guardian/receipts?playerId=${children[1].id}`, { scroll: false });
  fireEvent.change(filter, { target: { value: "" } });
  expect(router.push).toHaveBeenLastCalledWith("/guardian/receipts", { scroll: false });
  expect(screen.getByRole("link", { name: "التالي" })).toHaveAttribute("href", `/guardian/receipts?playerId=${children[0].id}&page=3`);
  expect(fetcher).toHaveBeenCalledWith(`/api/v1/guardian/subscriptions/receipts?page=2&allIssued=true&playerId=${children[0].id}`, expect.objectContaining({ cache: "no-store" }));
});

it("keeps the picker focused through a pending child read and a transient failure", async () => {
  let complete!: (value: Response) => void;
  const pending = new Promise<Response>(resolve => { complete = resolve; });
  const fetcher = vi.fn().mockResolvedValueOnce(feed([row], 1)).mockImplementationOnce(() => pending);
  vi.stubGlobal("fetch", fetcher);
  const view = render(<GuardianReceipts playerId="" page={1} />);
  const select = await screen.findByRole("combobox", { name: "اللاعب" });
  select.focus();
  view.rerender(<GuardianReceipts playerId={children[1].id} page={1} />);
  expect(select).toHaveFocus();
  expect(select).toHaveValue(children[1].id);
  expect(screen.queryByText(row.receiptNumber)).not.toBeInTheDocument();
  await act(async () => complete(Response.json({}, { status: 503 })));
  expect(await screen.findByRole("button", { name: "إعادة المحاولة" })).toBeVisible();
  expect(screen.getByRole("combobox", { name: "اللاعب" })).toBe(select);
});

it.each([401, 403, 404])("clears the remembered names and receipts on an unavailable read (%i)", async status => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(feed([row], 1)).mockResolvedValueOnce(Response.json({}, { status })));
  const view = render(<GuardianReceipts playerId="" page={1} />);
  await screen.findByRole("combobox", { name: "اللاعب" });
  view.rerender(<GuardianReceipts playerId={children[0].id} page={1} />);
  const recover = await screen.findByRole("link", { name: "عرض كل الإيصالات" });
  expect(recover).toHaveAttribute("href", "/guardian/receipts");
  expect(screen.queryByRole("combobox")).not.toBeInTheDocument();
  expect(screen.queryByText(row.receiptNumber)).not.toBeInTheDocument();
});

it("makes every authorized child selectable by ID while keeping one control", async () => {
  const manyChildren = Array.from({ length: 159 }, (_, index) => ({ id: `child-${index}`, arabicName: "لاعب اصطناعي" }));
  vi.stubGlobal("fetch", vi.fn(async () => Response.json({ items: [], children: manyChildren, total: 0, pageSize: 25 })));
  render(<GuardianReceipts playerId="" page={1} />);
  const select = await screen.findByRole("combobox", { name: "اللاعب" });
  expect(within(select).getAllByRole("option")).toHaveLength(160);
  fireEvent.change(select, { target: { value: manyChildren.at(-1)!.id } });
  expect(router.push).toHaveBeenCalledWith("/guardian/receipts?playerId=child-158", { scroll: false });
  expect(screen.getByText("لا توجد إيصالات دفع في حسابك بعد.")).toBeVisible();
});

it("labels duplicate names by the stored code and keeps filters tied to IDs", async () => {
  const sameName = "آدم العرض";
  const duplicates = [
    { ...children[0], arabicName: sameName, playerCode: "PLR-DEMO-A" },
    { ...children[1], arabicName: sameName, playerCode: "PLR-DEMO-B" },
    { id: "unique-child", arabicName: "ليلى العرض", playerCode: null },
  ];
  const payment = { ...row, currentPlayer: sameName, player: sameName, playerCode: duplicates[1].playerCode };
  vi.stubGlobal("fetch", vi.fn(async () => Response.json({ items: [payment], children: duplicates, total: 1, pageSize: 25 })));
  render(<GuardianReceipts playerId={duplicates[1].id} page={1} />);
  const select = await screen.findByRole("combobox", { name: "اللاعب" });
  expect(select).toHaveValue(duplicates[1].id);
  for (const child of duplicates.slice(0, 2)) {
    const option = within(select).getByRole("option", { name: new RegExp(child.playerCode!) });
    expect(option).toHaveAttribute("value", child.id);
    expect(option.textContent?.trim().startsWith(`\u2066${child.playerCode}\u2069`)).toBe(true);
  }
  expect(within(select).getByRole("option", { name: "ليلى العرض" })).toHaveAttribute("value", "unique-child");
  const receipt = screen.getByRole("link", { name: /RC-DEMO-012/ });
  expect(within(receipt).getByText("PLR-DEMO-B")).toHaveAttribute("dir", "ltr");
  expect(within(receipt).queryByText("PLR-DEMO-A")).not.toBeInTheDocument();
  fireEvent.change(select, { target: { value: duplicates[0].id } });
  expect(router.push).toHaveBeenCalledWith(`/guardian/receipts?playerId=${duplicates[0].id}`, { scroll: false });
});

it("keeps the stored amount, day and receipt source in the all-children scope", async () => {
  const historic = { ...row, id: "history", receiptNumber: "RC-HISTORY", amount: 600, finalAmount: 600, historicalPaidOn: "2026-09-01", isVoided: false };
  const tournament = { ...row, id: "tournament", receiptNumber: "TR-02", amount: 200, finalAmount: 200, source: "Tournament", isVoided: false };
  const paidForAnother = { ...row, id: "payer", receiptNumber: "RC-OTHER", currentPlayer: null, finalAmount: "737.60", linkedChild: false, isVoided: false };
  vi.stubGlobal("fetch", vi.fn(async () => feed([historic, tournament, paidForAnother], 3)));
  render(<GuardianReceipts playerId="" page={1} />);
  const historyCard = await screen.findByRole("link", { name: /RC-HISTORY/ });
  expect(historyCard).toHaveTextContent(/تحصيل سابق.*1 سبتمبر 2026/);
  const tournamentCard = screen.getByRole("link", { name: /TR-02/ });
  expect(tournamentCard).toHaveTextContent("رسم بطولة");
  expect(tournamentCard).toHaveAttribute("href", "/guardian/receipts/tournament?source=Tournament");
  const payerCard = screen.getByRole("link", { name: /RC-OTHER/ });
  expect(payerCard).toHaveTextContent("تجديد لغير الأبناء");
  expect(payerCard).toHaveTextContent("737.60 جنيه");
});

it("links an out-of-range child page straight to its last valid page", async () => {
  vi.stubGlobal("fetch", vi.fn(async () => feed([], 51)));
  render(<GuardianReceipts playerId={children[0].id} page={100000} />);
  expect(await screen.findByText("لا توجد إيصالات في هذه الصفحة.")).toBeVisible();
  expect(screen.getByRole("link", { name: "العودة إلى الصفحة الأخيرة" })).toHaveAttribute("href", `/guardian/receipts?playerId=${children[0].id}&page=3`);
  expect(screen.queryByRole("link", { name: "السابق" })).not.toBeInTheDocument();
});

it.each([
  { items: null, pageSize: 25 },
  { items: [row], pageSize: 0 },
  { items: [{ ...row, finalAmount: null, amount: null }], pageSize: 25 },
  { items: [{ ...row, finalAmount: "incorrect" }], pageSize: 25 },
])("shows retry for an invalid feed and restores a scoped empty state", async invalid => {
  const fetcher = vi.fn().mockResolvedValueOnce(Response.json({ ...invalid, children, total: 1 }))
    .mockResolvedValueOnce(feed([], 0));
  vi.stubGlobal("fetch", fetcher);
  render(<GuardianReceipts playerId={children[0].id} page={1} />);
  const retry = await screen.findByRole("button", { name: "إعادة المحاولة" });
  expect(screen.queryByText("0 جنيه")).not.toBeInTheDocument();
  expect(screen.queryByText(row.receiptNumber)).not.toBeInTheDocument();
  fireEvent.click(retry);
  expect(await screen.findByText("لا توجد إيصالات لهذا اللاعب.")).toBeVisible();
  expect(screen.getByRole("combobox", { name: "اللاعب" })).toHaveValue(children[0].id);
  expect(fetcher.mock.calls[0][0]).toBe(fetcher.mock.calls[1][0]);
});

it("ignores a delayed previous child and retries only the selected scope", async () => {
  let complete!: (data: Response) => void;
  const late = new Promise<Response>(resolve => { complete = resolve; });
  const fetcher = vi.fn()
    .mockImplementationOnce(() => late)
    .mockRejectedValueOnce(new TypeError("failed"))
    .mockResolvedValueOnce(feed([{ ...row, player: children[1].arabicName, currentPlayer: children[1].arabicName, receiptNumber: "RC-SCOPE-2" }]));
  vi.stubGlobal("fetch", fetcher);
  const view = render(<GuardianReceipts playerId={children[0].id} page={1} />);
  view.rerender(<GuardianReceipts playerId={children[1].id} page={1} />);
  await screen.findByRole("alert");
  await act(async () => complete(feed([row])));
  expect(screen.queryByText(row.receiptNumber)).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "إعادة المحاولة" }));
  expect(await screen.findByText("RC-SCOPE-2")).toBeVisible();
  expect(fetcher).toHaveBeenLastCalledWith(expect.stringContaining(children[1].id), expect.anything());
});

it("preserves the issued name and gives the guardian separate current identity context", async () => {
  vi.stubGlobal("fetch", vi.fn(async () => Response.json({
    academyName: "أكاديمية العرض", playerNameSnapshot: row.player,
    currentPlayer: row.currentPlayer, receiptNumber: row.receiptNumber,
    amount: row.amount, currency: row.currency, paidAtUtc: row.paidAtUtc,
    paymentMethod: "Cash",
  })));
  render(<ReceiptPrint endpoint="/receipt" />);
  expect(await screen.findByRole("heading", { name: row.player })).toBeVisible();
  expect(screen.getByText(`اللاعب المرتبط الآن: ${row.currentPlayer}`)).toHaveClass("receipt-linked-name");
  expect(screen.getByText("نقدًا")).toBeVisible();
});

it("matches the academy payment time between the family feed and the printable receipt", async () => {
  const payment = { ...row, paidAtUtc: "2026-11-11T22:00:00Z", paidAtAcademy: "2026-11-12T00:00:00+02:00" };
  vi.stubGlobal("fetch", vi.fn(async (endpoint: string) => endpoint === "/receipt"
    ? Response.json({ ...payment, isVoided: false }) : feed([payment], 1)));
  const { container } = render(<><GuardianReceipts playerId="" page={1} /><ReceiptPrint endpoint="/receipt" /></>);
  await screen.findByRole("article", { name: "إيصال دفع" });
  const times = [...container.querySelectorAll("time")];
  expect(times).toHaveLength(2);
  expect(times[0].textContent).toBe(times[1].textContent);
  expect(times[1]).toHaveTextContent(/12 نوفمبر 2026.*12:00 ص/);
  expect(times[1]).toHaveAttribute("datetime", payment.paidAtUtc);
  expect(screen.getByText("بتوقيت الأكاديمية")).toBeVisible();
});

it.each([undefined, "invalid", "2026-11-12T00:01:00+02:00"])("uses UTC for an absent or invalid academy timestamp %s", async paidAtAcademy => {
  vi.stubGlobal("fetch", vi.fn(async () => Response.json({
    ...row, isVoided: false, paidAtUtc: "2026-11-11T22:00:00Z", paidAtAcademy,
  })));
  const { container } = render(<ReceiptPrint endpoint="/receipt" />);
  expect(await screen.findByText("بالتوقيت العالمي")).toBeVisible();
  expect(container.querySelector("time")).toHaveTextContent(/11 نوفمبر 2026.*10:00 م/);
});

it("rejects an ambiguous payment time but keeps a date-only historical receipt", async () => {
  vi.stubGlobal("fetch", vi.fn(async (endpoint: string) => Response.json({
    ...row, isVoided: false, paidAtUtc: "2026-11-11T22:00:00",
    historicalPaidOn: endpoint === "/historical" ? "2026-11-11" : null,
  })));
  const view = render(<ReceiptPrint endpoint="/ambiguous" />);
  expect(await screen.findByRole("alert")).toHaveTextContent("الإيصال غير متاح");
  expect(screen.queryByRole("button", { name: "طباعة الإيصال" })).toBeNull();
  view.rerender(<ReceiptPrint endpoint="/historical" />);
  expect(await screen.findByText("تاريخ القبض")).toBeVisible();
  expect(view.container.querySelector("time")).toHaveTextContent(/^11 نوفمبر 2026$/);
  expect(screen.queryByText(/بتوقيت|بالتوقيت/)).toBeNull();
});

it.each([
  { method: "Cash", reference: "cash-operation-legacy", visible: false },
  { method: "Online", reference: "manual-1234567890abcdef1234567890abcdef", visible: false },
  { method: "Online", reference: "PSP-TRACK-101", visible: true },
])("renders the paid cents and keeps only a customer-facing $method reference", async ({ method, reference, visible }) => {
  vi.stubGlobal("fetch", vi.fn(async () => Response.json({
    receiptNumber: row.receiptNumber, playerNameSnapshot: row.player,
    amount: "737.60", originalAmount: 922, discountAmount: "184.40",
    currency: "EGP", paidAtUtc: row.paidAtUtc, paymentMethod: method, providerReference: reference,
  })));
  render(<ReceiptPrint endpoint="/receipt" />);
  const receipt = await screen.findByRole("article", { name: "إيصال دفع" });
  expect(receipt).toHaveTextContent("737.60 جنيه");
  expect(receipt).toHaveTextContent("184.40 جنيه");
  expect(receipt).toHaveTextContent(row.receiptNumber);
  if (visible) expect(receipt).toHaveTextContent(reference);
  else expect(receipt).not.toHaveTextContent(reference);
});

it.each([undefined, "  ", "unknown"])("does not print a receipt with missing or invalid paid amount %s", async amount => {
  vi.stubGlobal("fetch", vi.fn(async () => Response.json({
    receiptNumber: row.receiptNumber, currency: "EGP", paidAtUtc: row.paidAtUtc, amount,
  })));
  render(<ReceiptPrint endpoint="/receipt" />);
  expect(await screen.findByRole("alert")).toHaveTextContent("الإيصال غير متاح");
  expect(screen.queryByText("0 جنيه")).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "طباعة الإيصال" })).not.toBeInTheDocument();
});

it("shows a print action only for a confirmed tournament receipt", async () => {
  const record = { player: row.player, tournamentName: "بطولة العرض", amount: 200, currency: "EGP", receiptNumber: "TR-2026", confirmedAtUtc: row.paidAtUtc };
  const fetcher = vi.fn()
    .mockResolvedValueOnce(Response.json({ ...record, status: "Pending" }))
    .mockResolvedValueOnce(Response.json({ ...record, status: "Confirmed" }));
  vi.stubGlobal("fetch", fetcher);
  const view = render(<ReceiptPrint endpoint="/tournament-pending" tournament />);
  expect(await screen.findByRole("alert")).toHaveTextContent("الإيصال غير متاح");
  expect(screen.queryByRole("button", { name: "طباعة الإيصال" })).not.toBeInTheDocument();
  view.rerender(<ReceiptPrint endpoint="/tournament-confirmed" tournament />);
  expect(await screen.findByText("TR-2026")).toBeVisible();
  expect(screen.getByRole("button", { name: "طباعة الإيصال" })).toBeVisible();
  expect(screen.queryByText(/إثبات التحويل/)).not.toBeInTheDocument();
});
it("prints the historical receipt with the documented day and subscription period", async () => {
  const print = vi.spyOn(window, "print").mockImplementation(() => {});
  vi.stubGlobal("fetch", vi.fn(async () => new Response(JSON.stringify({
    receiptNumber: "HIST-600", playerNameSnapshot: "لاعب اختبار", sportNameSnapshot: "سباحة",
    originalAmount: 600, amount: 600, finalAmount: 600, discountAmount: 0, currency: "EGP",
    paidAtUtc: "2026-09-01T09:00:00Z", historicalPaidOn: "2026-09-01",
    historicalPeriod: { startDate: "2026-09-01", endDate: "2026-09-30" },
    paymentMethod: "Cash", providerReference: "HIST-INTERNAL",
  }))));
  render(<ReceiptPrint endpoint="/receipt" />);
  const document = await screen.findByRole("article", { name: "إيصال دفع" });
  expect(document).toHaveTextContent("600 جنيه");
  expect(document).toHaveTextContent("تاريخ القبض");
  expect(document).toHaveTextContent("فترة الاشتراك");
  expect(document).toHaveTextContent("تحصيل سابق");
  expect(document).not.toHaveTextContent("HIST-INTERNAL");
  expect(document).not.toHaveTextContent("تاريخ ووقت الدفع");
  expect([...document.querySelectorAll("time")].map(time => time.dateTime)).toEqual(["2026-09-01", "2026-09-01", "2026-09-30"]);
  fireEvent.click(screen.getByRole("button", { name: "طباعة الإيصال" }));
  expect(print).toHaveBeenCalledTimes(1);
  print.mockRestore();
  vi.unstubAllGlobals();
});
