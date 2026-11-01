import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import PlatformLogin from "./page";

const mocked = vi.hoisted(() => ({ replace: vi.fn(), request: vi.fn() }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ replace: mocked.replace }) }));
vi.mock("../../../features/platform-api", () => ({ platformRequest: mocked.request }));
afterEach(() => { cleanup(); vi.clearAllMocks(); });

it.each(["٠٢٤٦٨١", "۰۲۴۶۸۱"])("accepts six Arabic/Persian digits without losing leading zero: %s", async code => {
  mocked.request.mockResolvedValue({});
  render(<PlatformLogin />);
  fireEvent.change(screen.getByLabelText("البريد الإلكتروني"), { target: { value: "platform@example.test" } });
  fireEvent.change(screen.getByLabelText("كلمة المرور"), { target: { value: "Synthetic-Only-123!" } });
  fireEvent.change(screen.getByLabelText("رمز التحقق"), { target: { value: code } });
  expect(screen.getByLabelText("رمز التحقق")).toHaveValue("024681");
  fireEvent.click(screen.getByRole("button", { name: "دخول المنصة" }));
  await waitFor(() => expect(mocked.request).toHaveBeenCalledWith("/api/v1/platform/auth/login", "POST", { email: "platform@example.test", password: "Synthetic-Only-123!", code: "024681" }));
  expect(mocked.replace).toHaveBeenCalledWith("/platform");
});

it("retains the six-digit browser check for incomplete or punctuated input", () => {
  render(<PlatformLogin />);
  const code = screen.getByLabelText("رمز التحقق") as HTMLInputElement;
  for (const value of ["٢٤٦٨", "۲۴۶-۸۱", "abcdef"]) {
    fireEvent.change(code, { target: { value } });
    expect(code.checkValidity()).toBe(false);
  }
  expect(mocked.request).not.toHaveBeenCalled();
});
