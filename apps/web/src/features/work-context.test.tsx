import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { WorkContextChooser } from "./work-context";
import { WorkContextLink } from "./work-context-link";
const contexts = [
  { membershipId: "strong", academyId: "academy", academyName: "أكاديمية اصطناعية", role: "AcademyAdmin", allBranches: false, allSports: false, branchNames: ["فرع أ"], sportNames: ["كرة القدم"] },
  { membershipId: "weak", academyId: "academy", academyName: "أكاديمية اصطناعية", role: "AcademyAdmin", contextName: "متابعة السباحة", allBranches: false, allSports: false, branchNames: ["فرع ب"], sportNames: ["السباحة"] },
];
afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
it("requires explicit selection, displays correlated scope and navigates only after server confirmation", async () => {
  let writes: unknown[] = []; const navigate = vi.fn();
  vi.stubGlobal("fetch", vi.fn(async (url, init) => {
    if (String(url).endsWith("/csrf")) return Response.json({ token: "test" });
    if (String(url).endsWith("/session/academy")) { writes = [...writes, JSON.parse(init.body)]; return new Response(null, { status: 204 }); }
    return Response.json(contexts);
  }));
  render(<WorkContextChooser navigate={navigate} />);
  expect(screen.getByRole("button", { name: "فتح مساحة العمل" })).toBeDisabled();
  fireEvent.click(await screen.findByRole("radio", { name: /متابعة السباحة/ }));
  expect(screen.getByText(/فرع ب · السباحة/)).toBeVisible(); expect(navigate).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "فتح مساحة العمل" })); await waitFor(() => expect(navigate).toHaveBeenCalledOnce());
  expect(writes).toEqual([{ academyId: "academy", membershipId: "weak" }]);
});
it("keeps the chosen context and does not navigate when it has been revoked", async () => {
  const navigate = vi.fn(); vi.stubGlobal("fetch", vi.fn(async url => String(url).endsWith("/csrf") ? Response.json({ token: "test" }) : String(url).endsWith("/session/academy") ? Response.json({ title: "أوقف المالك هذا السياق" }, { status: 403 }) : Response.json(contexts)));
  render(<WorkContextChooser navigate={navigate} />); fireEvent.click(await screen.findByRole("radio", { name: /متابعة السباحة/ })); fireEvent.click(screen.getByRole("button", { name: "فتح مساحة العمل" }));
  expect(await screen.findByRole("alert")).toHaveTextContent("أوقف المالك"); expect(navigate).not.toHaveBeenCalled(); expect(screen.getByRole("radio", { name: /متابعة السباحة/ })).toBeChecked();
});
it("shows a switch link only for more than one web staff context", async () => {
  vi.stubGlobal("fetch", vi.fn(async () => Response.json([contexts[0], { role: "Guardian" }]))); const view = render(<WorkContextLink />); await waitFor(() => expect(fetch).toHaveBeenCalled()); expect(screen.queryByRole("link")).toBeNull(); view.unmount();
  vi.stubGlobal("fetch", vi.fn(async () => Response.json(contexts))); render(<WorkContextLink />); expect(await screen.findByRole("link", { name: "تبديل سياق العمل" })).toHaveAttribute("href", "/dashboard/context");
});
