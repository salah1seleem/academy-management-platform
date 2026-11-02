import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import LoginPage from "./page";

const replace = vi.hoisted(() => vi.fn());
vi.mock("next/navigation", () => ({ useRouter: () => ({ replace }) }));
afterEach(() => { cleanup(); vi.unstubAllGlobals(); replace.mockClear(); });

describe("Arabic authentication entry", () => {
  it("opens explicit context selection when the server returns multiple approved staff contexts", async () => {
    vi.stubGlobal("fetch", vi.fn(async url => String(url).endsWith("/csrf") ? Response.json({ token: "test" }) : Response.json({ authenticated: true, requiresContextSelection: true })));
    render(<LoginPage />); fireEvent.change(screen.getByLabelText("البريد الإلكتروني"), { target: { value: "admin@example.test" } }); fireEvent.change(screen.getByLabelText("كلمة المرور"), { target: { value: "Demo-Only-123!" } }); fireEvent.click(screen.getByRole("button", { name: "دخول آمن" }));
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/dashboard/context")); expect(replace).not.toHaveBeenCalledWith("/dashboard");
  });
  it("renders one dashboard login for owners and administrators without role tabs", () => {
    render(<LoginPage />);
    expect(screen.getByRole("heading", { name: "تسجيل الدخول" })).toBeInTheDocument();
    expect(screen.getByLabelText("البريد الإلكتروني")).toBeInTheDocument();
    expect(screen.getByLabelText("كلمة المرور")).toBeInTheDocument();
    expect(screen.queryByRole("tablist")).not.toBeInTheDocument();
    expect(screen.queryByText("فريق الأكاديمية")).not.toBeInTheDocument();
    expect(screen.queryByText("ولي أمر")).not.toBeInTheDocument();
    expect(screen.queryByLabelText("رقم الهاتف")).not.toBeInTheDocument();
  });
  it("shows an explicit logout action when another session is already active", async () => {
    vi.stubGlobal("fetch", vi.fn(async (url: unknown) => String(url).endsWith("/csrf") ? new Response(JSON.stringify({ token: "test" }), { status: 200 }) : new Response("{}", { status: 409 })));
    render(<LoginPage />);
    fireEvent.change(screen.getByLabelText("البريد الإلكتروني"), { target: { value: "admin@example.test" } });
    fireEvent.change(screen.getByLabelText("كلمة المرور"), { target: { value: "Demo-Only-123!" } });
    fireEvent.click(screen.getByRole("button", { name: "دخول آمن" }));
    expect(await screen.findByRole("alert")).toHaveTextContent("سجّل الخروج أولًا");
    expect(screen.getByRole("button", { name: "تسجيل خروج الحساب الحالي" })).toBeEnabled();
  });
});
