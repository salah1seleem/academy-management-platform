import { expect, type Page } from "@playwright/test";

export async function expectFeedbackState(page: Page, label: string) {
  await page.getByRole("tab", { name: "الملاحظة", exact: true }).click();
  await expect(page.getByRole("tabpanel", { name: "الملاحظة", exact: true }).getByText(label, { exact: true })).toBeVisible();
  await page.getByRole("tab", { name: "الرد والمتابعة", exact: true }).click();
}
