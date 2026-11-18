import { cleanup, render, screen } from "@testing-library/react";
import { afterEach, expect, it } from "vitest";
import { ImportSchemaGuide } from "./academy-import-schema";

const fields = Array.from({ length: 19 }, (_, i) => ({ key: `field-${i}`, label: i === 4 ? "الميلاد yyyy-MM-dd" : `حقل ${i + 1}`, required: i < 11 }));
const sheets = [{ name: "اللاعبون", optional: false, columns: fields }, { name: "ابدأ هنا", optional: true, columns: [] }];
afterEach(cleanup);
it("separates every required and optional field without record management tools", () => {
  render(<ImportSchemaGuide sheets={sheets} />);
  expect(screen.getByRole("heading", { name: "مطلوب (11)" })).toBeInTheDocument();
  expect(screen.getByRole("heading", { name: "اختياري (8)" })).toBeInTheDocument();
  expect(screen.getAllByRole("listitem", { hidden: true })).toHaveLength(19);
  expect(screen.queryByRole("table", { hidden: true })).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "الأعمدة", hidden: true })).not.toBeInTheDocument();
  const player = screen.getByText("اللاعبون").closest("details");
  expect(player).toHaveAttribute("open"); expect(screen.getByText("ابدأ هنا").closest("details")).not.toHaveAttribute("open");
});
it("preserves Arabic field labels and keeps their date notation LTR", () => {
  render(<ImportSchemaGuide sheets={sheets} />);
  expect(screen.getByText("yyyy-MM-dd")).toHaveAttribute("dir", "ltr");
  expect(screen.getByText("yyyy-MM-dd").closest("li")).toHaveTextContent("الميلاد yyyy-MM-dd");
});
it.each([{ name: "missing columns", sheets: [{ ...sheets[0], columns: null }] }, { name: "non-boolean required", sheets: [{ ...sheets[0], columns: [{ ...fields[0], required: "yes" }] }] }])("shows a read error for $name instead of an invented field list", failure => {
  render(<ImportSchemaGuide sheets={failure.sheets} />);
  expect(screen.getByRole("alert", { hidden: true })).toHaveTextContent("تعذرت قراءة دليل الحقول");
  expect(screen.queryByRole("heading", { name: /مطلوب/, hidden: true })).not.toBeInTheDocument();
});
