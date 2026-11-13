import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import PlatformLogin from "./page";

const replace = vi.hoisted(() => vi.fn());
vi.mock("next/navigation", () => ({ useRouter: () => ({ replace }) }));
afterEach(() => { cleanup(); vi.unstubAllGlobals(); replace.mockClear(); });

function fillCredentials() {
  fireEvent.change(screen.getByLabelText("البريد الإلكتروني"), { target: { value: "platform@example.test" } });
  fireEvent.change(screen.getByLabelText("كلمة المرور"), { target: { value: "Synthetic-Only-123!" } });
  return screen.getByLabelText("رمز التحقق");
}

it.each(["٠٢٤٦٨١", "۰۲۴۶۸۱"])("accepts six Arabic or Persian digits without losing leading zero: %s", async value => {
  const request = vi.fn<(url: string, options?: RequestInit) => Promise<Response>>(async url => url.endsWith("/csrf")
    ? Response.json({ token: "test" })
    : Response.json({ authenticated: true }));
  vi.stubGlobal("fetch", request);
  render(<PlatformLogin />);
  const code = fillCredentials();
  fireEvent.change(code, { target: { value } });
  expect(code).toHaveValue("024681");
  fireEvent.click(screen.getByRole("button", { name: "تسجيل الدخول" }));
  await waitFor(() => expect(replace).toHaveBeenCalledWith("/platform"));
  expect(request.mock.calls[1]?.[0]).toBe("/api/v1/platform/auth/login");
  expect(JSON.parse(String(request.mock.calls[1]?.[1]?.body))).toEqual({
    email: "platform@example.test", password: "Synthetic-Only-123!", code: "024681",
  });
});

it("explains an invalid code at its field before any request", () => {
  const request = vi.fn();
  vi.stubGlobal("fetch", request);
  render(<PlatformLogin />);
  const code = fillCredentials();
  for (const value of ["٢٤٦٨", "۲۴۶-۸۱", "abcdef"]) {
    fireEvent.change(code, { target: { value } });
    fireEvent.click(screen.getByRole("button", { name: "تسجيل الدخول" }));
    expect(code).toHaveFocus();
    expect(code).toHaveAccessibleDescription("اكتب رمز التحقق من ٦ أرقام.");
  }
  expect(request).not.toHaveBeenCalled();
});

it("treats a rejected sign-in as invalid credentials for a visitor", async () => {
  vi.stubGlobal("fetch", vi.fn(async (url: string) => url.endsWith("/csrf")
    ? Response.json({ token: "test" })
    : new Response("Internal authentication error", { status: 401 })));
  render(<PlatformLogin />);
  const code = fillCredentials();
  fireEvent.change(code, { target: { value: "123456" } });
  const submit = screen.getByRole("button", { name: "تسجيل الدخول" });
  fireEvent.click(submit);
  expect(await screen.findByRole("alert")).toHaveTextContent("تحقّق من البريد الإلكتروني وكلمة المرور ورمز التحقق.");
  expect(submit).toBeEnabled();
  expect(replace).not.toHaveBeenCalled();
});
