import { fireEvent, render, screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import LoginPage from "./page";

vi.mock("next/navigation", () => ({ useRouter: () => ({ replace: vi.fn() }) }));

describe("Arabic authentication entry", () => {
  it("renders staff login and the demo guardian disclosure", () => {
    const consoleError = vi.spyOn(console, "error").mockImplementation(() => undefined);
    render(<LoginPage />);
    expect(screen.getByRole("heading", { name: "تسجيل الدخول" })).toBeInTheDocument();
    expect(screen.getByLabelText("البريد الإلكتروني")).toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "ولي أمر" }));
    expect(screen.getByText(/لا يتم إرسال رسالة SMS حقيقية/)).toBeInTheDocument();
    expect(screen.getByLabelText("رقم الهاتف")).toHaveAttribute("dir", "ltr");
    expect(consoleError).not.toHaveBeenCalledWith(expect.stringContaining("uncontrolled input"));
    consoleError.mockRestore();
  });
});
