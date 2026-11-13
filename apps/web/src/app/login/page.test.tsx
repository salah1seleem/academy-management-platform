import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import LoginPage from "./page";

const replace = vi.hoisted(() => vi.fn());
vi.mock("next/navigation", () => ({ useRouter: () => ({ replace }) }));
afterEach(() => { cleanup(); vi.unstubAllGlobals(); replace.mockClear(); });

describe("Arabic authentication entry", () => {
  it("opens explicit context selection when the server returns multiple approved staff contexts", async () => {
    vi.stubGlobal("fetch", vi.fn(async url => String(url).endsWith("/csrf") ? Response.json({ token: "test" }) : Response.json({ authenticated: true, requiresContextSelection: true })));
    render(<LoginPage />); fireEvent.change(screen.getByLabelText("البريد الإلكتروني"), { target: { value: "admin@example.test" } }); fireEvent.change(screen.getByLabelText("كلمة المرور"), { target: { value: "Demo-Only-123!" } }); fireEvent.click(screen.getByRole("button", { name: "تسجيل الدخول" }));
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/dashboard/context")); expect(replace).not.toHaveBeenCalledWith("/dashboard");
  });
  it("renders one dashboard login for owners and administrators without role tabs", () => {
    render(<LoginPage />);
    expect(screen.getByRole("heading", { name: "تسجيل دخول الأكاديمية" })).toBeInTheDocument();
    expect(screen.getByLabelText("البريد الإلكتروني")).toBeInTheDocument();
    expect(screen.getByLabelText("كلمة المرور")).toBeInTheDocument();
    expect(screen.queryByRole("tablist")).not.toBeInTheDocument();
    expect(screen.queryByText("فريق الأكاديمية")).not.toBeInTheDocument();
    expect(screen.queryByText("ولي أمر")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("رقم الهاتف")).not.toBeInTheDocument();
  });
  it("retains both recovery choices when signing out of an active session fails", async () => {
    const request = vi.fn(async (url: string) => {
      if (url.endsWith("/csrf")) return Response.json({ token: "test" });
      return new Response(null, { status: url.endsWith("/login") ? 409 : 503 });
    });
    vi.stubGlobal("fetch", request);
    render(<LoginPage />);
    fireEvent.change(screen.getByLabelText("البريد الإلكتروني"), { target: { value: "admin@example.test" } });
    fireEvent.change(screen.getByLabelText("كلمة المرور"), { target: { value: "Demo-Only-123!" } });
    fireEvent.click(screen.getByRole("button", { name: "تسجيل الدخول" }));
    expect(await screen.findByRole("heading", { name: "حساب مفتوح بالفعل" })).toHaveFocus();
    expect(screen.queryByLabelText("كلمة المرور")).not.toBeInTheDocument();
    const logout = screen.getByRole("button", { name: "تسجيل الخروج لتبديل الحساب" });
    fireEvent.click(logout);
    expect(screen.queryByRole("link", { name: "متابعة بالحساب الحالي" })).not.toBeInTheDocument();
    expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تسجيل الخروج");
    expect(logout).toBeEnabled();
    expect(screen.getByRole("link", { name: "متابعة بالحساب الحالي" })).toHaveAttribute("href", "/");
    expect(request.mock.calls.filter(([url]) => url.endsWith("/logout"))).toHaveLength(1);
  });
  it("focuses the first invalid field with an Arabic message before requesting a token", () => {
    const request = vi.fn();
    vi.stubGlobal("fetch", request);
    render(<LoginPage />);
    const email = screen.getByLabelText("البريد الإلكتروني");
    const password = screen.getByLabelText("كلمة المرور");
    const submit = screen.getByRole("button", { name: "تسجيل الدخول" });
    fireEvent.change(email, { target: { value: "invalid-email" } });
    fireEvent.click(submit);
    expect(email).toHaveFocus();
    expect(email).toHaveAccessibleDescription("اكتب بريدًا إلكترونيًا صحيحًا.");
    fireEvent.change(email, { target: { value: "admin@example.test" } });
    expect(email).not.toHaveAttribute("aria-invalid");
    fireEvent.click(submit);
    expect(password).toHaveFocus();
    expect(password).toHaveAccessibleDescription("اكتب كلمة المرور.");
    expect(request).not.toHaveBeenCalled();
  });
});
