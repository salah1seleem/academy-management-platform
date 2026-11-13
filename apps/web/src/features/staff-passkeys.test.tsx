import { act, cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { StaffPasskeySettings } from "./staff-passkeys";

const key = { id: "key", name: "هاتف العمل", isActive: true, createdAtUtc: "2026-10-16T10:00:00Z", mayBeSynced: true };
const token = () => Response.json({ token: "test" });
const list = (items: unknown[], configured = true) => Response.json({ configured, items });
const options = () => Response.json({
  challengeId: "challenge", purpose: "Register",
  options: { challenge: "AQ", rp: { id: "localhost", name: "عرض" }, user: { id: "Ag", name: "عرض", displayName: "عرض" },
    pubKeyCredParams: [{ type: "public-key", alg: -7 }], authenticatorSelection: { userVerification: "required" } },
});
const deviceCredential = () => ({
  id: "AQ", type: "public-key", rawId: new Uint8Array([1]).buffer, authenticatorAttachment: "platform",
  getClientExtensionResults: () => ({}),
  response: { clientDataJSON: new Uint8Array([2]).buffer, attestationObject: new Uint8Array([3]).buffer, getTransports: () => ["internal"] },
});

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

function support(create: (options: CredentialCreationOptions) => Promise<unknown>) {
  vi.stubGlobal("isSecureContext", true);
  vi.stubGlobal("PublicKeyCredential", class {});
  vi.stubGlobal("navigator", { credentials: { create } });
}

async function addMethod() {
  const button = await screen.findByRole("button", { name: "إضافة وسيلة تحقق" });
  fireEvent.change(screen.getByLabelText("اسم الوسيلة"), { target: { value: key.name } });
  fireEvent.click(button);
}

it("separates loading, failed reads and a successful unavailable configuration", async () => {
  let reads = 0;
  const fetcher = vi.fn(async () => ++reads === 1 ? new Response("server error", { status: 503 }) : list([], false));
  vi.stubGlobal("fetch", fetcher);
  render(<StaffPasskeySettings />);
  expect(screen.getByRole("status")).toHaveTextContent("جارٍ تحميل وسائل التحقق");
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل وسائل التحقق");
  expect(screen.queryByText(/لا توجد وسائل/)).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "إعادة التحميل" }));
  expect(await screen.findByText(/إضافة وسيلة تحقق غير متاحة/)).toBeVisible();
  expect(screen.queryByRole("button", { name: "إضافة وسيلة تحقق" })).not.toBeInTheDocument();
  expect(await screen.findByText(/لا توجد وسائل/)).toBeVisible();
});

it("requests device verification only after a valid name and reports the saved credential", async () => {
  const create = vi.fn(async (request: CredentialCreationOptions) => {
    expect(request.publicKey?.authenticatorSelection?.userVerification).toBe("required");
    return deviceCredential();
  });
  support(create);
  let saved = false;
  const fetcher = vi.fn(async (path: string, init?: RequestInit) => {
    if (path.endsWith("/csrf")) return token();
    if (path.endsWith("/register/options")) return options();
    if (path.endsWith("/register/complete")) {
      expect(JSON.parse(String(init?.body))).toMatchObject({ challengeId: "challenge", credential: { rawId: "AQ", response: { attestationObject: "Aw" } } });
      expect(String(init?.body)).not.toContain("biometric");
      saved = true;
      return Response.json({ verified: true, purpose: "Register", credential: key, attendanceRecorded: false });
    }
    return list(saved ? [key] : []);
  });
  vi.stubGlobal("fetch", fetcher);
  render(<StaffPasskeySettings />);
  fireEvent.click(await screen.findByRole("button", { name: "إضافة وسيلة تحقق" }));
  expect(screen.getByLabelText("اسم الوسيلة")).toHaveAccessibleDescription("اكتب اسمًا من حرفين إلى ٨٠ حرفًا.");
  expect(screen.getByLabelText("اسم الوسيلة")).toHaveFocus();
  expect(create).not.toHaveBeenCalled();
  await addMethod();
  expect(await screen.findByRole("status")).toHaveTextContent("تمت إضافة وسيلة «هاتف العمل».");
  expect(screen.getByRole("button", { name: "إيقاف هاتف العمل" })).toBeEnabled();
  expect(create).toHaveBeenCalledOnce();
});

it("traps focus in the selected confirmation and restores focus on Escape", async () => {
  const fetcher = vi.fn(async () => list([key], false));
  vi.stubGlobal("fetch", fetcher);
  render(<StaffPasskeySettings />);
  const opener = await screen.findByRole("button", { name: "إيقاف هاتف العمل" });
  opener.focus();
  fireEvent.click(opener);
  const dialog = screen.getByRole("dialog", { name: "إيقاف وسيلة «هاتف العمل»؟" });
  expect(dialog).toHaveAttribute("aria-modal", "true");
  expect(within(dialog).getByRole("button", { name: "إلغاء" })).toHaveFocus();
  fireEvent.keyDown(dialog, { key: "Tab" });
  expect(within(dialog).getByRole("button", { name: "تأكيد الإيقاف" })).toHaveFocus();
  fireEvent.keyDown(dialog, { key: "Escape" });
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  expect(opener).toHaveFocus();
  expect(fetcher).toHaveBeenCalledOnce();
});

it("verifies an unconfirmed stop by reading the same credential without replay", async () => {
  const calls: string[] = [];
  let stopped = false;
  let failedCheck = false;
  vi.stubGlobal("fetch", vi.fn(async (path: string, init?: RequestInit) => {
    if (path.endsWith("/csrf")) return token();
    if (init?.method === "POST") {
      calls.push(path);
      stopped = true;
      return new Response(null, { status: 503 });
    }
    if (stopped && !failedCheck) {
      failedCheck = true;
      return new Response(null, { status: 503 });
    }
    return list([{ ...key, isActive: !stopped }], false);
  }));
  render(<StaffPasskeySettings />);
  fireEvent.click(await screen.findByRole("button", { name: "إيقاف هاتف العمل" }));
  fireEvent.click(screen.getByRole("button", { name: "تأكيد الإيقاف" }));
  expect(screen.getByRole("button", { name: "جارٍ إيقاف الوسيلة…" })).toBeDisabled();
  const check = await screen.findByRole("button", { name: "تحقّق من حالة «هاتف العمل»" });
  await waitFor(() => expect(check).toHaveFocus());
  expect(screen.getByRole("button", { name: "إيقاف هاتف العمل" })).toBeDisabled();
  fireEvent.click(check);
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر التحقق من الحالة");
  fireEvent.click(check);
  expect(await screen.findByRole("status")).toHaveTextContent("تم إيقاف وسيلة «هاتف العمل».");
  expect(screen.getByText("متوقفة")).toBeVisible();
  expect(calls).toEqual(["/api/v1/staff/passkeys/credentials/key/revoke"]);
});

it("a lost registration response refreshes the list without claiming a different key was saved", async () => {
  support(async () => deviceCredential());
  let writes = 0;
  vi.stubGlobal("fetch", vi.fn(async (path: string) => {
    if (path.endsWith("/csrf")) return token();
    if (path.endsWith("/register/options")) return options();
    if (path.endsWith("/register/complete")) {
      writes++;
      return new Response(null, { status: 503 });
    }
    return list(writes ? [{ ...key, id: "other" }] : []);
  }));
  render(<StaffPasskeySettings />);
  await addMethod();
  const verify = await screen.findByRole("button", { name: "تحقّق من الوسائل المحفوظة" });
  expect(screen.getByRole("button", { name: "إضافة وسيلة تحقق" })).toBeDisabled();
  fireEvent.click(verify);
  expect(await screen.findByRole("status")).toHaveTextContent("تم تحديث القائمة.");
  expect(screen.getByRole("button", { name: "إضافة وسيلة تحقق" })).toBeEnabled();
  expect(writes).toBe(1);
});

it("a dismissed browser prompt is recoverable without a credential write", async () => {
  support(async () => { throw new DOMException("Denied", "NotAllowedError"); });
  const fetcher = vi.fn(async (path: string) => path.endsWith("/csrf") ? token() : path.endsWith("/options") ? options() : list([]));
  vi.stubGlobal("fetch", fetcher);
  render(<StaffPasskeySettings />);
  await addMethod();
  expect(await screen.findByRole("alert")).toHaveTextContent("لم يكتمل التحقق على الجهاز");
  expect(screen.getByRole("button", { name: "إضافة وسيلة تحقق" })).toBeEnabled();
  await waitFor(() => expect(fetcher.mock.calls.some(([path]) => path.endsWith("/complete"))).toBe(false));
  expect(screen.queryByText("Denied")).not.toBeInTheDocument();
});

it("leaving the screen cancels the device prompt and prevents a late save", async () => {
  let deliver!: (proof: unknown) => void;
  const pending = new Promise(resolve => { deliver = resolve; });
  const create = vi.fn((request: CredentialCreationOptions) => {
    expect(request.signal?.aborted).toBe(false);
    return pending;
  });
  support(create);
  const fetcher = vi.fn(async (path: string) => path.endsWith("/csrf") ? token() : path.endsWith("/options") ? options() : list([]));
  vi.stubGlobal("fetch", fetcher);
  const page = render(<StaffPasskeySettings />);
  await addMethod();
  await waitFor(() => expect(create).toHaveBeenCalledOnce());
  page.unmount();
  expect(create.mock.calls[0][0].signal?.aborted).toBe(true);
  await act(async () => deliver(deviceCredential()));
  expect(fetcher.mock.calls.some(([path]) => path.endsWith("/complete"))).toBe(false);
});
