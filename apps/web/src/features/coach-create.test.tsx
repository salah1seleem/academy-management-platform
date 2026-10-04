import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CoachCreate } from "./coach-create";

const { push, request } = vi.hoisted(() => ({ push: vi.fn(), request: vi.fn() }));
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }));
vi.mock("./dashboard-api", () => ({ csrfRequest: request }));
beforeEach(() => {
  vi.clearAllMocks();
  vi.stubGlobal("fetch", vi.fn().mockResolvedValue({ ok: true, json: async () => ({ groups: [{ id: "group-a", arabicName: "براعم 2018" }] }) }));
});
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });

async function fill() {
  fireEvent.change(await screen.findByLabelText("الاسم بالعربية"), { target: { value: "مدرب تجريبي" } });
  fireEvent.change(screen.getByLabelText("رقم الهاتف المصري"), { target: { value: "01012345678" } });
}
describe("individual coach provisioning", () => {
  it("submits name, phone, optional email, active status and selected groups without a shared password", async () => {
    request.mockResolvedValue({ json: async () => ({ membershipId: "member-a" }) });
    render(<CoachCreate />); await fill();
    fireEvent.click(screen.getByLabelText("براعم 2018"));
    expect(screen.queryByLabelText(/كلمة المرور/)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole("button", { name: "حفظ المدرب" }));
    await waitFor(() => expect(request).toHaveBeenCalledWith("/api/v1/manage/coaches", "POST", {
      displayName: "مدرب تجريبي", phoneNumber: "01012345678", email: null, isActive: true, groupIds: ["group-a"],
    }));
    await waitFor(() => expect(push).toHaveBeenCalledWith("/dashboard/academy/coaches/member-a"));
  });
  it("keeps the form and exposes the server validation failure", async () => {
    request.mockRejectedValue(new Error("تعذر ربط الهوية بهذه البيانات."));
    render(<CoachCreate />); await fill(); fireEvent.click(screen.getByRole("button", { name: "حفظ المدرب" }));
    expect(await screen.findByText("تعذر ربط الهوية بهذه البيانات.")).toBeVisible();
    expect(push).not.toHaveBeenCalled(); expect(screen.getByLabelText("رقم الهاتف المصري")).toHaveValue("01012345678");
  });
  it("prevents duplicate clicks while a create is pending", async () => {
    request.mockReturnValue(new Promise(() => {})); render(<CoachCreate />); await fill();
    fireEvent.click(screen.getByRole("button", { name: "حفظ المدرب" }));
    expect(await screen.findByRole("button", { name: "جارٍ الحفظ…" })).toBeDisabled(); expect(request).toHaveBeenCalledOnce();
  });
  it("does not show a writable form when group loading fails", async () => {
    vi.mocked(fetch).mockRejectedValue(new Error("offline")); render(<CoachCreate />);
    expect(await screen.findByText(/تعذر تحميل المجموعات/)).toBeVisible();
    expect(screen.queryByRole("button", { name: "حفظ المدرب" })).not.toBeInTheDocument();
  });
});
