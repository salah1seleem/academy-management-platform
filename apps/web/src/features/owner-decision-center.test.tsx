import { cleanup, fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { OwnerDecisionCenter } from "./owner-decision-center";

const query = new URLSearchParams();
vi.mock("next/navigation", () => ({ useSearchParams: () => query }));
afterEach(() => { cleanup(); vi.unstubAllGlobals(); for (const key of [...query.keys()]) query.delete(key); });

it.each([undefined, "Absence", "Capacity"])("explains a forbidden decision read without offering actions (%s)", async (kind) => {
  if (kind) query.set("kind", kind);
  vi.stubGlobal("fetch", vi.fn(async () => new Response(null, { status: 403 })));
  render(<OwnerDecisionCenter />);
  expect(await screen.findByRole("alert")).toHaveTextContent("حسابك لا يملك صلاحية عرض قرارات المالك");
  expect(screen.queryByRole("button", { name: /إعادة/ })).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "دمج مجموعات" })).not.toBeInTheDocument();
});

it("opens an existing follow-up, and uses one pager for the server's player pages", async () => {
  query.set("kind", "Absence");
  const reads = vi.fn(async (url: string) => {
    const page = new URL(url, "http://localhost").searchParams.get("page");
    const items = page === "1" ? Array.from({ length: 50 }, (_, i) => ({
      playerId: `p-${i}`, enrollmentId: `e-${i}`, name: `لاعب العرض ${i + 1}`,
      branchId: "b", branch: "فرع العرض", sportId: "s", sport: "السباحة", taskId: i === 0 ? "open-task" : null,
    })) : [];
    return Response.json({ total: 51, items });
  });
  vi.stubGlobal("fetch", reads);
  render(<OwnerDecisionCenter />);
  const table = await screen.findByRole("table", { name: "غياب ثلاث حصص متتالية" });
  expect(within(table).getByRole("link", { name: "عرض المتابعة" })).toHaveAttribute("href", "/dashboard/operations/follow-ups/open-task");
  expect(within(table).getAllByRole("row")).toHaveLength(51);
  expect(screen.getAllByRole("button", { name: "التالي" })).toHaveLength(1);
  fireEvent.click(screen.getByRole("button", { name: "التالي" }));
  expect(await screen.findByText("صفحة 2")).toBeVisible();
  expect(reads.mock.calls[1][0]).toContain("page=2");
});

it("retains the exact merge preview and idempotency key when the confirmation response is lost", async () => {
  query.set("kind", "Capacity");
  const writes: { body: unknown; key: string | null }[] = [];
  const groups = {
    today: "2026-11-05", groups: [
      { groupId: "g1", name: "مجموعة أ", branchId: "b", branch: "الفرع", sportId: "s", sport: "السباحة", occupied: 3, available: 1 },
      { groupId: "g2", name: "مجموعة ب", branchId: "b", branch: "الفرع", sportId: "s", sport: "السباحة", occupied: 2, available: 5 },
    ],
  };
  vi.stubGlobal("fetch", vi.fn(async (url: string, init?: RequestInit) => {
    if (url.endsWith("/csrf")) return Response.json({ token: "test" });
    if (url.endsWith("/preview")) return Response.json({ fingerprint: "reviewed", count: 3, source: "مجموعة أ", target: "مجموعة ب", conflicts: [] });
    if (url.endsWith("/merge")) {
      writes.push({ body: JSON.parse(String(init?.body)), key: new Headers(init?.headers).get("Idempotency-Key") });
      if (writes.length === 1) throw new TypeError("Failed to fetch");
      return Response.json({});
    }
    return Response.json(groups);
  }));
  render(<OwnerDecisionCenter />);
  fireEvent.click(await screen.findByRole("button", { name: "دمج مجموعات" }));
  const source = await screen.findByLabelText("المجموعة المنقول منها");
  fireEvent.change(source, { target: { value: "g1" } });
  fireEvent.change(screen.getByLabelText("المجموعة المستقبلة"), { target: { value: "g2" } });
  fireEvent.click(screen.getByRole("button", { name: "معاينة الدمج" }));
  fireEvent.click(await screen.findByRole("button", { name: "تأكيد الدمج" }));
  expect(await screen.findByRole("alert")).toHaveTextContent("لم يصل تأكيد الدمج");
  expect(source).toBeDisabled();
  expect(screen.queryByText("تم نقل اللاعبين إلى المجموعة المستقبلة.")).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "إعادة تأكيد نفس الدمج" }));
  expect(await screen.findByText("تم نقل اللاعبين إلى المجموعة المستقبلة.")).toBeVisible();
  expect(writes).toHaveLength(2);
  expect(writes[0].key).toBeTruthy();
  expect(writes[1]).toEqual(writes[0]);
});
