import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { GuardianFeedbackInbox } from "./guardian-feedback";
import FeedbackPage from "../app/dashboard/feedback/page";

afterEach(() => { cleanup(); vi.restoreAllMocks(); });
it("keeps guardian complaints private in an owner inbox with a clear review action", async () => {
  global.fetch = vi.fn(input => String(input).includes("options") ? Promise.resolve(Response.json({ branches: [] })) : Promise.resolve(Response.json([{ id: "note-1", kind: "Complaint", score: null, message: "اقتراح موعد أوضح", status: "Open", createdAtUtc: "2026-10-08T06:00:00Z", playerId: "player-1", playerName: "لاعب تجريبي", branchName: "فرع تجريبي", coachName: "مدرب تجريبي" }]))) as typeof fetch;
  render(<GuardianFeedbackInbox />);
  expect(await screen.findByRole("table", { name: "ملاحظات أولياء الأمور" })).toHaveTextContent("اقتراح موعد أوضح");
  expect(screen.getByRole("link", { name: "لاعب تجريبي" })).toHaveAttribute("href", "/dashboard/players/player-1");
  expect(screen.getByRole("button", { name: "تسجيل المراجعة" })).toBeVisible();
  fireEvent.click(screen.getByRole("button", { name: "الرد والحل" }));
  expect(screen.getByRole("textbox", { name: "الرد على الملاحظة" })).toBeVisible();
});
it("preserves the unresolved owner deep-link filter", async () => {
  global.fetch = vi.fn(input => String(input).includes("options") ? Promise.resolve(Response.json({ branches: [] })) : Promise.resolve(Response.json([]))) as typeof fetch;
  render(await FeedbackPage({ searchParams: Promise.resolve({ kind: "Complaint", status: "Unresolved" }) }));
  expect(screen.getByRole("combobox", { name: "الحالة" })).toHaveValue("Unresolved");
});
