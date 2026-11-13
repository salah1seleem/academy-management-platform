import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { FamilyRenewalRequest, FamilyRenewalRequests, FamilyTransferProof, type FamilyRequest } from "./family-renewal-request";

const { upload } = vi.hoisted(() => ({ upload: vi.fn(async () => ({ id: "proof", url: "/api/v1/assets/proof" })) }));
vi.mock("./dashboard-api", async original => ({ ...await original<typeof import("./dashboard-api")>(), uploadAsset: upload }));
afterEach(() => { cleanup(); vi.restoreAllMocks(); upload.mockClear(); });
const response = (data: unknown, status = 200) => new Response(JSON.stringify(data), { status });
const request: FamilyRequest = { id: "family", revision: 2, canReview: true, status: "Pending", lineCount: 2, originalAmount: "2000.00", discountAmount: "400.00", totalAmount: "1600.00", currency: "EGP", proofUrl: "/api/v1/assets/proof", items: ["آدم اختبار", "لينا اختبار"].map((player, i) => ({ id: `line${i}`, player, sport: "كرة القدم", branch: "الفرع", plan: "شهر", originalAmount: "1000.00", discountAmount: "200.00", finalAmount: "800.00", currency: "EGP", groups: [{ id: "group", name: "مجموعة اختبار" }] })) };
describe("family payment handoff", () => {
  it("FR-02 FR-06: shows fixed groups and stages the financial decision before sending", async () => {
    const assigned = { ...request, items: request.items.map(line => ({ ...line, groupId: "group" })) };
    const transport = vi.fn(async () => response(assigned));
    global.fetch = transport;
    render(<FamilyRenewalRequest id="family" staff />);
    const approve = await screen.findByRole("button", { name: "قبول وتجديد كل التسجيلات" });
    fireEvent.click(approve);
    const dialog = await screen.findByRole("dialog", { name: "تأكيد اعتماد تجديد الأسرة" });
    expect(dialog).toHaveTextContent("1,600 جنيه");
    expect(dialog).toHaveTextContent("تسجيلين");
    expect(transport).toHaveBeenCalledTimes(1);
    fireEvent.keyDown(dialog, { key: "Escape" });
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    expect(screen.queryByRole("combobox")).not.toBeInTheDocument();
    expect(screen.getAllByText("مجموعة اختبار")).toHaveLength(2);
  });
  it("hides review actions when the server grants read only", async () => { global.fetch = vi.fn(async () => response({ ...request, canReview: false })); render(<FamilyRenewalRequest id="family" staff />); await screen.findByText(/هذا الطلب للقراءة فقط/); expect(screen.queryByRole("button", { name: /قبول|رفض/ })).not.toBeInTheDocument(); expect(screen.queryByRole("combobox")).not.toBeInTheDocument(); });
  it("requires all groups and submits one atomic staff decision", async () => {
    const bodies: Record<string, unknown>[] = []; global.fetch = vi.fn(async (path, init) => { if (String(path).endsWith("/csrf")) return response({ token: "csrf" }); if (init?.method === "POST") { bodies.push(JSON.parse(String(init.body))); return response({ ...request, status: "Approved", items: request.items.map(l => ({ ...l, receiptId: `receipt-${l.id}` })) }); } return response(request); });
    render(<FamilyRenewalRequest id="family" staff />); await screen.findByText("آدم اختبار"); const accept = screen.getByRole("button", { name: "قبول وتجديد كل التسجيلات" }); expect(accept).toBeDisabled();
    fireEvent.change(screen.getByLabelText("مجموعة آدم اختبار"), { target: { value: "group" } }); expect(accept).toBeDisabled(); fireEvent.change(screen.getByLabelText("مجموعة لينا اختبار"), { target: { value: "group" } }); fireEvent.click(accept); expect(bodies).toHaveLength(0);
    fireEvent.click(screen.getByRole("button", { name: "تأكيد الاعتماد" })); await screen.findByText("تم تجديد الأسرة"); expect(bodies).toEqual([{ accept: true, revision: 2, reason: "", placements: [{ lineId: "line0", groupId: "group" }, { lineId: "line1", groupId: "group" }] }]); expect(screen.getByRole("link", { name: "إيصال آدم اختبار" })).toHaveAttribute("href", "/dashboard/subscriptions/receipts/receipt-line0"); expect(screen.queryByRole("button", { name: /قبول/ })).not.toBeInTheDocument();
  });
  it("rejects the whole request without requiring groups or reason", async () => {
    let sent: unknown; global.fetch = vi.fn(async (path, init) => String(path).endsWith("/csrf") ? response({ token: "csrf" }) : init?.method === "POST" ? (sent = JSON.parse(String(init.body)), response({ ...request, status: "Rejected" })) : response({ ...request, proofUrl: null }));
    render(<FamilyRenewalRequest id="family" staff />); const reject = await screen.findByRole("button", { name: "رفض الطلب كاملًا" });
    expect(screen.getByRole("button", { name: "قبول وتجديد كل التسجيلات" })).toBeDisabled();
    fireEvent.click(reject); expect(sent).toBeUndefined();
    fireEvent.click(screen.getByRole("button", { name: "تأكيد رفض الطلب" }));
    await screen.findByText("لم يُقبل الطلب"); expect(sent).toEqual({ accept: false, revision: 2, reason: "", placements: null });
  });
  it("shows proof received without another upload or activation claim", async () => { global.fetch = vi.fn(async () => response(request)); render(<FamilyRenewalRequest id="family" />); await screen.findByText(/وصل إثبات الدفع/); expect(screen.queryByText("تحويل واحد للأسرة")).not.toBeInTheDocument(); expect(screen.queryByText("تم تجديد الأسرة")).not.toBeInTheDocument(); });
  it("asks before cancellation and sends only the displayed revision", async () => {
    let sent: unknown; global.fetch = vi.fn(async (path, init) => String(path).endsWith("/csrf") ? response({ token: "csrf" }) : init?.method === "POST" ? (sent = JSON.parse(String(init.body)), response({ ...request, status: "Cancelled" })) : response(request));
    render(<FamilyRenewalRequest id="family" />); fireEvent.click(await screen.findByRole("button", { name: "إلغاء طلب التجديد" })); expect(sent).toBeUndefined(); expect(screen.getByText(/لا يسترد أي تحويل/)).toBeVisible(); fireEvent.click(screen.getByRole("button", { name: "تأكيد إلغاء الطلب" })); await screen.findByText("ملغي"); expect(sent).toEqual({ revision: 2 });
  });
  it("fails closed for unavailable family requests", async () => { global.fetch = vi.fn(async () => response({}, 404)); render(<FamilyRenewalRequest id="other" staff />); await screen.findByRole("alert"); expect(screen.queryByText("آدم اختبار")).not.toBeInTheDocument(); expect(screen.queryByRole("button")).not.toBeInTheDocument(); });
  it("replaces guardian loading with an unavailable error and a way back", async () => {
    global.fetch = vi.fn(async () => response({}, 404));
    render(<FamilyRenewalRequest id="other" />);
    expect(screen.getByText("جارٍ التحميل…")).toBeVisible();
    expect(await screen.findByRole("alert")).toHaveTextContent("طلب الأسرة غير متاح");
    expect(screen.queryByText("جارٍ التحميل…")).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "العودة للطلبات" })).toHaveAttribute("href", "/guardian/family-renewals");
    expect(screen.queryByRole("button", { name: /التحويل|إثبات|إلغاء/ })).not.toBeInTheDocument();
  });
  it("retains the uploaded asset when confirmation is lost", async () => {
    const saved = vi.fn(), bodies: unknown[] = []; let attempts = 0;
    global.fetch = vi.fn(async (path, init) => { if (String(path).endsWith("payment-settings")) return response({ configured: true, instaPayAddress: "synthetic@example.test" }); if (String(path).endsWith("/csrf")) return response({ token: "csrf" }); bodies.push(JSON.parse(String(init?.body))); if (++attempts === 1) throw new TypeError("INTERNAL"); return response(request); });
    render(<FamilyTransferProof id="family" revision={1} onSaved={saved} />); await screen.findByText("synthetic@example.test"); fireEvent.change(screen.getByLabelText("صورة إثبات الأسرة"), { target: { files: [new File(["synthetic"], "demo.png", { type: "image/png" })] } }); fireEvent.click(screen.getByRole("button", { name: "إرسال إثبات الأسرة" })); await screen.findByText(/تعذر تأكيد العملية/); expect(upload).toHaveBeenCalledTimes(1); expect(screen.getByRole("button", { name: "اختيار صورة إيصال التحويل" })).toBeDisabled(); fireEvent.click(screen.getByRole("button", { name: "إعادة تأكيد نفس الإثبات" })); await waitFor(() => expect(saved).toHaveBeenCalledOnce()); expect(upload).toHaveBeenCalledTimes(1); expect(bodies).toEqual([{ assetId: "proof", revision: 1 }, { assetId: "proof", revision: 1 }]);
  });
  it("does not allow proof upload without a configured transfer account", async () => { global.fetch = vi.fn(async () => response({ configured: false })); render(<FamilyTransferProof id="family" revision={1} onSaved={vi.fn()} />); await screen.findByText(/لم تضبط الأكاديمية حساب التحويل/); expect(screen.queryByRole("button", { name: "إرسال إثبات الأسرة" })).not.toBeInTheDocument(); });
  it("lists stored family requests with one detail link per request", async () => { global.fetch = vi.fn(async () => response({ items: [{ id: "family", lineCount: 2, amount: "1600.00", currency: "EGP", status: "Pending", createdAtUtc: "2026-09-28T10:00:00Z" }] })); render(<FamilyRenewalRequests />); await screen.findByText("بانتظار المراجعة · 2 تسجيل"); expect(screen.getByRole("link", { name: /بانتظار المراجعة/ })).toHaveAttribute("href", "/guardian/family-renewals/family"); });

  it("FR-04: blocks approval for an ineligible assignment and displays its reason", async () => {
    const transport = vi.fn(async () => response({ ...request, items: request.items.map((line, index) =>
      index === 0 ? { ...line, groupId: "missing" } : { ...line, groups: [] }) }));
    global.fetch = transport;
    render(<FamilyRenewalRequest id="family" staff />);
    expect(await screen.findByText(/راجع أهلية المجموعة الحالية/)).toBeVisible();
    expect(screen.getByText("لا توجد مجموعة مناسبة لهذا التسجيل.")).toBeVisible();
    expect(screen.getByRole("button", { name: "قبول وتجديد كل التسجيلات" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "رفض الطلب كاملًا" })).toBeEnabled();
    expect(transport).toHaveBeenCalledTimes(1);
  });

  it.each(["conflict", "unavailable", "foreign"] as const)("FR-10 FR-12 FR-13: waits for readback after %s", async (kind) => {
    let readCount = 0;
    const writes: unknown[] = [];
    const latest = { ...request, revision: 3 };
    global.fetch = vi.fn(async (path, init) => {
      if (String(path).endsWith("/csrf")) return response({ token: "csrf" });
      if (init?.method !== "POST") return response(++readCount === 1 ? request : latest);
      writes.push(JSON.parse(String(init.body)));
      if (writes.length > 1) return response({ ...latest, status: "Rejected", decisionReason: "إثبات غير مطابق" });
      if (kind === "conflict") return response({ title: "تغير الطلب." }, 409);
      if (kind === "foreign") return response({ ...request, id: "other", status: "Rejected", decisionReason: "خاص بطلب مختلف" });
      throw new TypeError("Failed to fetch");
    });
    render(<FamilyRenewalRequest id="family" staff />);
    const reason = await screen.findByLabelText(/ملاحظة أو سبب الرفض/);
    fireEvent.change(reason, { target: { value: "إثبات غير مطابق" } });
    fireEvent.click(screen.getByRole("button", { name: "رفض الطلب كاملًا" }));
    expect(screen.getByRole("dialog")).toHaveTextContent("إثبات غير مطابق");
    fireEvent.click(screen.getByRole("button", { name: "تأكيد رفض الطلب" }));
    fireEvent.click(await screen.findByRole("button", { name: "التحقق من حالة الطلب" }));
    expect(writes).toHaveLength(1);
    const reject = await screen.findByRole("button", { name: "رفض الطلب كاملًا" });
    expect(screen.queryByText("خاص بطلب مختلف")).not.toBeInTheDocument();
    expect(screen.getByLabelText(/ملاحظة أو سبب الرفض/)).toHaveValue("إثبات غير مطابق");
    fireEvent.click(reject);
    fireEvent.click(screen.getByRole("button", { name: "تأكيد رفض الطلب" }));
    await screen.findByText("لم يُقبل الطلب");
    expect(writes).toEqual([
      { accept: false, revision: 2, reason: "إثبات غير مطابق", placements: null },
      { accept: false, revision: 3, reason: "إثبات غير مطابق", placements: null },
    ]);
  });

  it("FR-15: verifies a committed cancellation by reading it after a lost response", async () => {
    let reads = 0;
    let writes = 0;
    global.fetch = vi.fn(async (path, init) => {
      if (String(path).endsWith("/csrf")) return response({ token: "csrf" });
      if (init?.method === "POST") {
        writes++;
        throw new TypeError("Failed to fetch");
      }
      if (!String(path).endsWith("/family/requests/family")) return response({}, 404);
      return response(++reads === 1 ? request : { ...request, revision: 3, status: "Cancelled" });
    });
    render(<FamilyRenewalRequest id="family" />);
    fireEvent.click(await screen.findByRole("button", { name: "إلغاء طلب التجديد" }));
    fireEvent.click(screen.getByRole("button", { name: "تأكيد إلغاء الطلب" }));
    fireEvent.click(await screen.findByRole("button", { name: "التحقق من حالة الطلب" }));
    await screen.findByText("ملغي");
    expect(writes).toBe(1);
    expect(screen.queryByRole("button", { name: "إلغاء طلب التجديد" })).not.toBeInTheDocument();
  });

  it("FR-16: retries loading a transfer account and handles clipboard denial", async () => {
    let loads = 0;
    global.fetch = vi.fn(async () => ++loads === 1 ? response({}, 503) : response({ configured: true, walletNumber: "01000000001" }));
    render(<FamilyTransferProof id="family" revision={1} onSaved={vi.fn()} />);
    await screen.findByRole("alert");
    expect(screen.queryByRole("button", { name: "إرسال إثبات الأسرة" })).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "إعادة تحميل حساب التحويل" }));
    const copy = await screen.findByRole("button", { name: "نسخ المحفظة" });
    const clipboard = { writeText: vi.fn().mockRejectedValue(new Error("Permission denied")) };
    vi.stubGlobal("navigator", { clipboard });
    try {
      fireEvent.click(copy);
      await screen.findByText(/تعذر النسخ/);
      expect(clipboard.writeText).toHaveBeenCalledWith("01000000001");
      expect(screen.getByText("01000000001")).toBeVisible();
    } finally { vi.unstubAllGlobals(); }
  });

  it("FR-19: never retains a previous family's data while navigating", async () => {
    global.fetch = vi.fn(async path => String(path).endsWith("/family") ? response(request) : response({}, 404));
    const view = render(<FamilyRenewalRequest id="family" staff />);
    await screen.findByText("آدم اختبار");
    view.rerender(<FamilyRenewalRequest id="other" staff />);
    expect(screen.queryByText("آدم اختبار")).not.toBeInTheDocument();
    await screen.findByRole("alert");
    expect(screen.queryByRole("button", { name: "رفض الطلب كاملًا" })).not.toBeInTheDocument();
  });
});
