import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { BeneficiaryRenewalCode } from "./beneficiary-renewal-code";

const oldRevision = "a".repeat(32);
const newRevision = "b".repeat(32);
const code = `RNW-${"A".repeat(24)}`;
const expiresAtUtc = "2027-09-28T00:00:00Z";
const empty = { revision: "none", expiresAtUtc: null };
const active = { revision: oldRevision, expiresAtUtc };
const props = { enrollmentId: "swimming", player: "لاعب اصطناعي", sport: "السباحة", branch: "فرع التجمع الخامس", onClose: vi.fn() };
const json = (body: unknown, status = 200) => Response.json(body, { status });
const button = (name: string) => screen.getByRole("button", { name });

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

it("issues one code, shows it once and supports manual copy when clipboard access fails", async () => {
  let finish!: (response: Response) => void;
  const pending = new Promise<Response>(resolve => { finish = resolve; });
  const writeText = vi.fn().mockRejectedValueOnce(new Error("Denied")).mockResolvedValueOnce(undefined);
  vi.stubGlobal("navigator", { ...navigator, clipboard: { writeText } });
  const fetch = vi.fn(async (path: string, init?: RequestInit) => {
    if (path.endsWith("/csrf")) return json({ token: "csrf" });
    if (init?.method === "POST") {
      expect(new Headers(init.headers).get("If-Match")).toBe('"none"');
      return pending;
    }
    return json(empty);
  });
  vi.stubGlobal("fetch", fetch);
  const { unmount } = render(<BeneficiaryRenewalCode {...props} />);
  const issue = await screen.findByRole("button", { name: "إصدار كود" });
  expect(screen.getByText(/السباحة · فرع التجمع الخامس/)).toBeVisible();
  expect(fetch.mock.calls.every(([, init]) => init?.method !== "POST")).toBe(true);
  fireEvent.click(issue);
  fireEvent.click(issue);
  await waitFor(() => expect(fetch.mock.calls.filter(([, init]) => init?.method === "POST")).toHaveLength(1));
  await act(async () => finish(json({ reference: code, revision: newRevision, expiresAtUtc })));
  const output = screen.getByRole("textbox", { name: "الكود الجديد" });
  expect(output).toHaveValue(code);
  fireEvent.click(button("نسخ الكود"));
  expect(await screen.findByText("تعذر النسخ تلقائيًا. انسخ الكود المحدد.")).toBeVisible();
  expect(output).toHaveFocus();
  fireEvent.click(button("نسخ الكود"));
  expect(await screen.findByText("تم نسخ الكود.")).toBeVisible();
  expect(writeText).toHaveBeenCalledWith(code);
  unmount();
  fetch.mockResolvedValueOnce(json(active));
  render(<BeneficiaryRenewalCode {...props} />);
  expect(await screen.findByRole("button", { name: "استبدال الكود" })).toBeVisible();
  expect(screen.queryByRole("textbox", { name: "الكود الجديد" })).not.toBeInTheDocument();
});

it("confirms replacement and rereads a stale revision without sending a second write", async () => {
  let reads = 0, writes = 0;
  vi.stubGlobal("fetch", vi.fn(async (path: string, init?: RequestInit) => {
    if (path.endsWith("/csrf")) return json({ token: "csrf" });
    if (init?.method === "POST") {
      writes++;
      expect(new Headers(init.headers).get("If-Match")).toBe(`"${oldRevision}"`);
      return json({}, 412);
    }
    return json(reads++ ? { ...active, revision: newRevision } : active);
  }));
  render(<BeneficiaryRenewalCode {...props} />);
  const replacement = await screen.findByRole("button", { name: "استبدال الكود" });
  fireEvent.click(replacement);
  expect(screen.getByRole("dialog")).toHaveTextContent("يتوقف الكود السابق عن العمل");
  fireEvent.click(button("إلغاء"));
  expect(writes).toBe(0);
  fireEvent.click(replacement);
  fireEvent.click(button("إصدار كود جديد"));
  expect(await screen.findByRole("alert")).toHaveTextContent("تغيّرت حالة الكود");
  expect(screen.queryByRole("button", { name: "استبدال الكود" })).not.toBeInTheDocument();
  fireEvent.click(button("تحقّق من الحالة"));
  expect(await screen.findByRole("button", { name: "استبدال الكود" })).toBeVisible();
  expect(writes).toBe(1);
});

it("requires an explicit status read after a lost issuance response", async () => {
  let saved = false, writes = 0;
  vi.stubGlobal("fetch", vi.fn(async (path: string, init?: RequestInit) => {
    if (path.endsWith("/csrf")) return json({ token: "csrf" });
    if (init?.method === "POST") { writes++; saved = true; throw new TypeError("network"); }
    return json(saved ? active : empty);
  }));
  render(<BeneficiaryRenewalCode {...props} />);
  fireEvent.click(await screen.findByRole("button", { name: "إصدار كود" }));
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تأكيد إصدار الكود");
  fireEvent.click(button("تحقّق من الحالة"));
  expect(await screen.findByRole("button", { name: "استبدال الكود" })).toBeVisible();
  expect(screen.queryByRole("textbox", { name: "الكود الجديد" })).not.toBeInTheDocument();
  expect(writes).toBe(1);
});

it("ignores a late status read from the previously selected enrollment", async () => {
  let finish!: (response: Response) => void;
  const pending = new Promise<Response>(resolve => { finish = resolve; });
  vi.stubGlobal("fetch", vi.fn(async (path: string) => path.endsWith("/swimming") ? pending : json(empty)));
  const { rerender } = render(<BeneficiaryRenewalCode key="swimming" {...props} />);
  expect(screen.getByRole("status")).toHaveTextContent("جارٍ قراءة حالة الكود");
  rerender(<BeneficiaryRenewalCode key="football" {...props} enrollmentId="football" sport="كرة القدم" />);
  expect(await screen.findByRole("button", { name: "إصدار كود" })).toBeVisible();
  await act(async () => finish(json(active)));
  expect(button("إصدار كود")).toBeVisible();
  expect(screen.queryByRole("button", { name: "استبدال الكود" })).not.toBeInTheDocument();
});

it("recovers from a failed state read without exposing the write action", async () => {
  vi.stubGlobal("fetch", vi.fn().mockResolvedValueOnce(json({}, 503)).mockResolvedValueOnce(json(empty)));
  render(<BeneficiaryRenewalCode {...props} />);
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر قراءة حالة الكود");
  expect(screen.queryByRole("button", { name: "إصدار كود" })).not.toBeInTheDocument();
  fireEvent.click(button("تحقّق من الحالة"));
  expect(await screen.findByRole("button", { name: "إصدار كود" })).toBeVisible();
});
