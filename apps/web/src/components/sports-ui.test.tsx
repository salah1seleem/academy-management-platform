import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { AttendanceChoice, IdentityMark, ProgressTrack, SportsHero, SportsQuickActions } from "./sports-ui";

afterEach(cleanup);
describe("sports dashboard presentation", () => {
  it("uses the supplied academy, identity and report reference date", () => {
    render(<SportsHero name="صلاح التجريبي" academy="أكاديمية النجوم" date="2026-09-28" />);
    expect(screen.getByRole("heading", { name: "أهلًا، صلاح التجريبي" })).toBeVisible();
    expect(screen.getByText(/ملخص أداء أكاديمية النجوم/)).toBeVisible();
    expect(screen.getByText(/مرجع التقرير/)).toBeVisible();
    expect(screen.queryByText(/زيادة|مقارنة|%/)).not.toBeInTheDocument();
  });
  it("keeps coach shortcuts operational with no financial or people management links", () => {
    render(<SportsQuickActions coach />);
    expect(screen.getAllByRole("link")).toHaveLength(2);
    expect(screen.queryByRole("link", { name: /المالي|تجديد|تسجيل لاعب/ })).not.toBeInTheDocument();
  });
  it("does not expose an unimplemented coach provisioning shortcut", () => {
    render(<SportsQuickActions />);
    expect(screen.getAllByRole("link")).toHaveLength(5);
    expect(screen.queryByRole("link", { name: /إضافة مدرب/ })).not.toBeInTheDocument();
  });
  it("renders real progress and keeps zero empty", () => {
    const { rerender } = render(<ProgressTrack value={2} total={5} label="الحضور" />);
    expect(screen.getByRole("progressbar")).toHaveAttribute("aria-valuetext", "2 من 5");
    expect(screen.getByRole("progressbar").firstElementChild).toHaveStyle("--progress: 40%");
    rerender(<ProgressTrack value={0} total={0} label="الحضور" />);
    expect(screen.getByRole("progressbar").firstElementChild).toHaveStyle("--progress: 0%");
  });
  it("communicates attendance selection with text and aria, not just color", () => {
    const onClick = vi.fn(); render(<AttendanceChoice value="Absent" selected onClick={onClick} />);
    const button = screen.getByRole("button", { name: "غائب" });
    expect(button).toHaveAttribute("aria-pressed", "true"); fireEvent.click(button); expect(onClick).toHaveBeenCalledOnce();
  });
  it("uses initials instead of inventing a child photograph", () => {
    render(<IdentityMark name="عمر خالد" detail="NG-001" />);
    expect(screen.getByText("عمر خالد")).toBeVisible(); expect(screen.getByText("NG-001")).toBeVisible();
    expect(screen.queryByRole("img")).not.toBeInTheDocument();
  });
});
