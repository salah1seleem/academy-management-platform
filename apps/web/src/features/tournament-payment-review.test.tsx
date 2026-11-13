import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { TournamentPaymentReview } from "./matches";

const id = "tournament-payment";
const asset = "48197aab-584b-4e12-a2cb-7d54a975d741";
const pending = {
  id, tournamentName: "بطولة اصطناعية", player: "لاعب اصطناعي", amount: 250, currency: "EGP",
  status: "Pending", hasProof: true, proofAssetId: asset, proofUrl: `/api/v1/assets/${asset}`,
  reviewReason: null, receiptNumber: null, canReview: true,
};
const saved = { ...pending, status: "Confirmed", receiptNumber: "TRN-TEST-20261112" };

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

async function confirmCollection() {
  fireEvent.click(await screen.findByRole("button", { name: "قبول وإصدار الإيصال" }));
  expect(screen.getByRole("dialog")).toHaveTextContent("250 جنيه");
  fireEvent.click(screen.getByRole("button", { name: "تسجيل التحصيل" }));
}

it("requires readback after confirmation and never displays stale decision controls after a failed read", async () => {
  let savedOnServer = false;
  let reads = 0;
  const writes: unknown[] = [];
  vi.stubGlobal("fetch", vi.fn(async (path, options) => {
    if (String(path).endsWith("/csrf")) return Response.json({ token: "test" });
    if (String(path).endsWith("/review")) {
      writes.push(JSON.parse(options.body));
      savedOnServer = true;
      return Response.json({ receiptNumber: saved.receiptNumber });
    }
    if (++reads === 2) return new Response(null, { status: 503 });
    return Response.json(savedOnServer ? saved : pending);
  }));
  render(<TournamentPaymentReview id={id} />);
  await confirmCollection();
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تأكيد القرار");
  expect(screen.queryByText(saved.receiptNumber)).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "رفض الطلب" })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "إعادة قراءة الطلب" }));
  expect(await screen.findByText(saved.receiptNumber)).toBeVisible();
  expect(writes).toEqual([{ accept: true, reason: null, expectedProofAssetId: asset }]);
});

it("recovers a matching saved receipt when the review response is lost", async () => {
  let wrote = false;
  vi.stubGlobal("fetch", vi.fn(async path => {
    if (String(path).endsWith("/csrf")) return Response.json({ token: "test" });
    if (String(path).endsWith("/review")) {
      wrote = true;
      throw new TypeError("Connection interrupted");
    }
    return Response.json(wrote ? saved : pending);
  }));
  render(<TournamentPaymentReview id={id} />);
  await confirmCollection();
  expect(await screen.findByText(saved.receiptNumber)).toBeVisible();
  expect(screen.queryByRole("alert")).not.toBeInTheDocument();
});

it("keeps the proof visible to a read-only staff member without exposing review actions", async () => {
  vi.stubGlobal("fetch", vi.fn(async () => Response.json({ ...pending, canReview: false })));
  render(<TournamentPaymentReview id={id} />);
  expect(await screen.findByRole("link", { name: "عرض إثبات التحويل" })).toHaveAttribute("href", pending.proofUrl);
  expect(screen.queryByRole("button", { name: "رفض الطلب" })).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "قبول وإصدار الإيصال" })).not.toBeInTheDocument();
});

it("shows a payment permission message in place of an inaccessible proof link", async () => {
  vi.stubGlobal("fetch", vi.fn(async () => Response.json({ ...pending, proofUrl: null, proofAssetId: null, canReview: false })));
  render(<TournamentPaymentReview id={id} />);
  expect(await screen.findByText("عرض الإثبات يتطلب صلاحية قراءة المدفوعات.")).toBeVisible();
  expect(screen.getByText("قيد المراجعة")).toBeVisible();
  expect(screen.queryByRole("link", { name: "عرض إثبات التحويل" })).not.toBeInTheDocument();
});
