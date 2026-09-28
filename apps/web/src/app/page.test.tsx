import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import HomePage from "./page";

describe("Arabic RTL foundation shell", () => {
  it("renders the title, role previews, and the development disclaimer", () => {
    render(<HomePage />);

    expect(
      screen.getByRole("heading", { name: "منصة إدارة الأكاديمية" }),
    ).toBeInTheDocument();
    expect(screen.getByText("مالك الأكاديمية")).toBeInTheDocument();
    expect(screen.getByText("الإداري")).toBeInTheDocument();
    expect(screen.getByText("المدرب")).toBeInTheDocument();
    expect(screen.getByText("ولي الأمر")).toBeInTheDocument();
    expect(screen.getByRole("note")).toHaveTextContent("لا يوجد تسجيل دخول");
  });
});
