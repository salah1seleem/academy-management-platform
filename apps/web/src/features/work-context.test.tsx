import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { WorkContextChooser } from "./work-context";
import { WorkContextLink } from "./work-context-link";

const contexts = [
  { membershipId: "strong", academyId: "academy", academyName: "أكاديمية اصطناعية", role: "AcademyAdmin", allBranches: false, allSports: false, branchNames: ["فرع أ"], sportNames: ["كرة القدم"] },
  { membershipId: "weak", academyId: "academy", academyName: "أكاديمية اصطناعية", role: "AcademyAdmin", contextName: "متابعة السباحة", allBranches: false, allSports: false, branchNames: ["فرع ب"], sportNames: ["السباحة"] },
];

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

function submit() {
  fireEvent.click(screen.getByRole("button", { name: "فتح مساحة العمل" }));
}

async function chooseSwimming() {
  const radio = await screen.findByRole("radio", { name: /متابعة السباحة/ });
  fireEvent.click(radio);
  return radio;
}

it("requires a choice and opens the selected workspace after server confirmation", async () => {
  const writes: unknown[] = [];
  const navigate = vi.fn();
  vi.stubGlobal("fetch", vi.fn(async (url: string, init?: RequestInit) => {
    if (url.endsWith("/csrf")) return Response.json({ token: "test" });
    if (url.endsWith("/me")) return Response.json(contexts[0]);
    if (url.endsWith("/session/academy")) {
      writes.push(JSON.parse(String(init?.body)));
      return new Response(null, { status: 204 });
    }
    return Response.json(contexts);
  }));

  render(<WorkContextChooser navigate={navigate} />);
  await screen.findByText("مساحة العمل الحالية");
  expect(screen.getByRole("button", { name: "فتح مساحة العمل" })).toBeDisabled();
  expect(screen.getByRole("link", { name: "متابعة بالمساحة الحالية" })).toHaveAttribute("href", "/dashboard");
  expect(await chooseSwimming()).toHaveAccessibleDescription("الفروع: فرع ب الرياضات: السباحة");
  expect(navigate).not.toHaveBeenCalled();
  submit();
  await waitFor(() => expect(navigate).toHaveBeenCalledOnce());
  expect(writes).toEqual([{ academyId: "academy", membershipId: "weak" }]);
});

it("distinguishes loading, a failed read, and a successful empty result", async () => {
  let reads = 0;
  vi.stubGlobal("fetch", vi.fn(async (url: string) => {
    if (url.endsWith("/me")) return new Response(null, { status: 403 });
    return ++reads === 1 ? new Response(null, { status: 503 }) : Response.json([{ role: "Guardian" }]);
  }));

  render(<WorkContextChooser />);
  expect(screen.getByRole("status")).toHaveTextContent("جارٍ تحميل مساحات العمل");
  expect(screen.queryByRole("button", { name: "فتح مساحة العمل" })).not.toBeInTheDocument();
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل مساحات العمل");
  fireEvent.click(screen.getByRole("button", { name: "إعادة التحميل" }));
  expect(await screen.findByText("لا توجد مساحات عمل متاحة لهذا الحساب.")).toBeVisible();
  expect(screen.queryByRole("link", { name: "متابعة بالمساحة الحالية" })).not.toBeInTheDocument();
});

it("refreshes revoked choices without replaying the change", async () => {
  const navigate = vi.fn();
  let revoked = false;
  let writes = 0;
  vi.stubGlobal("fetch", vi.fn(async (url: string) => {
    if (url.endsWith("/me")) return new Response(null, { status: 403 });
    if (url.endsWith("/csrf")) return Response.json({ token: "test" });
    if (url.endsWith("/session/academy")) {
      revoked = true;
      writes++;
      return new Response(null, { status: 403 });
    }
    return Response.json(revoked ? [contexts[0]] : contexts);
  }));

  render(<WorkContextChooser navigate={navigate} />);
  await chooseSwimming();
  submit();
  expect(await screen.findByRole("alert")).toHaveTextContent("حدّث الاختيارات قبل المتابعة");
  fireEvent.click(screen.getByRole("button", { name: "تحديث الاختيارات" }));
  expect(await screen.findByRole("radio", { name: /الإدارة الأساسية/ })).not.toBeChecked();
  expect(screen.queryByRole("radio", { name: /متابعة السباحة/ })).not.toBeInTheDocument();
  expect(writes).toBe(1);
  expect(navigate).not.toHaveBeenCalled();
});

it.each([true, false])("reads the active membership after a lost response (committed: %s)", async committed => {
  const navigate = vi.fn();
  let writes = 0;
  vi.stubGlobal("fetch", vi.fn(async (url: string) => {
    if (url.endsWith("/me")) return Response.json(writes && committed ? contexts[1] : contexts[0]);
    if (url.endsWith("/csrf")) return Response.json({ token: "test" });
    if (url.endsWith("/session/academy")) {
      writes++;
      throw new TypeError("Connection closed");
    }
    return Response.json(contexts);
  }));

  render(<WorkContextChooser navigate={navigate} />);
  const radio = await chooseSwimming();
  submit();
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تأكيد فتح مساحة العمل");
  expect(radio).toBeDisabled();
  expect(screen.queryByRole("button", { name: "فتح مساحة العمل" })).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button", { name: "تحقّق من المساحة الحالية" }));
  if (committed) {
    await waitFor(() => expect(navigate).toHaveBeenCalledOnce());
  } else {
    expect(await screen.findByRole("button", { name: "فتح مساحة العمل" })).toBeEnabled();
    expect(navigate).not.toHaveBeenCalled();
  }
  expect(writes).toBe(1);
});

it("preserves an allowlisted destination when the session expires", async () => {
  vi.stubGlobal("fetch", vi.fn(async () => new Response(null, { status: 401 })));
  render(<WorkContextChooser returnTo="/dashboard/evaluations/new" />);
  expect(await screen.findByRole("link", { name: "تسجيل الدخول" })).toHaveAttribute("href", "/login?returnTo=%2Fdashboard%2Fevaluations%2Fnew");
});

it("shows a switch link only for more than one web staff context", async () => {
  const request = vi.fn(async () => Response.json([contexts[0], { role: "Guardian" }]));
  vi.stubGlobal("fetch", request);
  const view = render(<WorkContextLink />);
  await waitFor(() => expect(request).toHaveBeenCalled());
  expect(screen.queryByRole("link")).toBeNull();
  view.unmount();

  request.mockResolvedValueOnce(Response.json(contexts));
  render(<WorkContextLink />);
  expect(await screen.findByRole("link", { name: "تبديل سياق العمل" })).toHaveAttribute("href", "/dashboard/context");
});
