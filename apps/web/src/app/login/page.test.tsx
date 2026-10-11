import { render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import LoginPage from "./page";

vi.mock("next/navigation", () => ({ useRouter: () => ({ replace: vi.fn() }) }));

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
});
