import { test } from "@playwright/test";
import { placementId as id, placementWrite } from "../support/placement";

// A read-only visual inventory visit, not acceptance of a business scenario.
// Actions and their cases are added only after reviewing these cloud images.
test("Visual review only: group capacity editor before scenario design", async ({ page }, info) => {
  const response = await placementWrite(page.request, "/api/v1/auth/login", { email: "owner.nogoom@example.test", password: "Demo-Only-123!" });
  if (response.status() !== 200) throw new Error("Synthetic owner login failed during visual setup");
  await page.goto(`/dashboard/academy/groups/${id(90)}/edit`);
  await page.getByRole("heading", { name: "تعديل المجموعة", exact: true }).waitFor({ state: "visible" });
  await page.getByLabel("السعة", { exact: true }).waitFor({ state: "visible" });
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 900 });
    await page.screenshot({ path: info.outputPath(`group-editor-before-${width}.png`), fullPage: true, animations: "disabled" });
  }
});
