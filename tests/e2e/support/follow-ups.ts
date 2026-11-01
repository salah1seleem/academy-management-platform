import { expect, type Page } from "@playwright/test";

/** The current list contains saved tasks; operating conditions live in its creation form. */
export async function openOperationalFollowUp(page: Page, candidate: {
  source: { groupId: string }; condition: string; activeFollowUpId?: string | null;
}) {
  await page.getByRole("link", { name: "إنشاء متابعة", exact: true }).click();
  await page.getByRole("combobox", { name: "مصدر المتابعة", exact: true })
    .selectOption(`${candidate.source.groupId}:${candidate.condition}`);
  if (candidate.activeFollowUpId) {
    const existing = page.getByRole("link", { name: "فتح المتابعة القائمة", exact: true });
    await expect(existing).toHaveAttribute("href", `/dashboard/operations/follow-ups/${candidate.activeFollowUpId}`);
    await existing.click();
  } else {
    await page.getByRole("button", { name: "حفظ المتابعة", exact: true }).click();
  }
  await page.waitForURL(/follow-ups\/[a-f0-9-]+$/);
}
