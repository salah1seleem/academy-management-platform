import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { FeedbackDeadlineEditor, FeedbackDeadlineEvidence } from "./feedback-deadline-policy";
afterEach(cleanup);
it("requires explicit enablement, calendar days and preserves old intake", () => {
  const change = vi.fn(); render(<FeedbackDeadlineEditor disabled={false} onChange={change}/>);
  expect(screen.queryByRole("spinbutton")).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("checkbox")); expect(change).toHaveBeenCalledWith(3);
  expect(screen.getByText(/بل.*السلامة يحتاج مراجعة بشرية/)).toBeInTheDocument();
});
it("shows the captured policy rather than the current configuration", () => {
  render(<FeedbackDeadlineEvidence evidence={JSON.stringify({revision:7,days:2,receivedOn:"2026-10-19",dueOn:"2026-10-21",timeZone:"Africa/Cairo"})}/>);
  expect(screen.getByRole("note")).toHaveTextContent("الإصدار 7"); expect(screen.getByRole("note")).toHaveTextContent("2026-10-21");
});
it("does not invent a policy for legacy records or render broken JSON", () => {
  const { rerender } = render(<FeedbackDeadlineEvidence/>); expect(screen.getByText(/لم تحفظ سياسة/)).toBeInTheDocument();
  rerender(<FeedbackDeadlineEvidence evidence="broken"/>); expect(screen.getByText(/تعذر عرض دليل/)).toBeInTheDocument();
});
