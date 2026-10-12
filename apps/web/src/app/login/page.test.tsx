import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import LoginPage from "./page";

vi.mock("next/navigation", () => ({ useRouter: () => ({ replace: vi.fn() }) }));
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

describe("Arabic authentication entry", () => {
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
