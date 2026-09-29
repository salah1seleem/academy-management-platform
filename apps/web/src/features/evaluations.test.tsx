import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { CriteriaList, CriterionForm, EvaluationEditor, EvaluationReport, EvaluationsList, GuardianEvaluationHistory } from "./evaluations";

const push = vi.fn();
vi.mock("next/navigation", () => ({ useRouter: () => ({ push, back: vi.fn() }) }));

const footballReport = {
  id: "evaluation-1", player: "عمر أحمد محمود", photoReference: "/icon.svg", footballPosition: "جناح أيمن", sport: "كرة القدم", evaluationDate: "2026-09-28", reportingPeriod: "سبتمبر 2026", evaluator: "كريم حسن", generalNotes: "تطور ملحوظ", age: 10, heightCm: 142, weightKg: 38, preferredFoot: "Right", isFootballReport: true, overallScore: 78.5, scoredCriteria: 2, totalApplicableCriteria: 3, completenessPercentage: 66.7, availableAxes: 2,
  axes: [{ axis: "Passing", value: 82 }, { axis: "Dribbling", value: 77 }, { axis: "Speed", value: undefined }, { axis: "Defending", value: undefined }, { axis: "Physical", value: undefined }, { axis: "Shooting", value: undefined }],
  criteria: [{ name: "دقة التمرير", score: 84, notes: "جيد", axis: "Passing" }, { name: "الرؤية", score: 81, axis: "Passing" }, { name: "التمركز", score: undefined }]
};

beforeEach(() => { push.mockReset(); });
afterEach(cleanup);

describe("evaluation experience", () => {
  it("renders the criteria list with sport, axis, weight and functional actions", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => [{ id: "c1", sportId: "s1", sport: "كرة القدم", arabicName: "دقة التمرير", displayOrder: 1, isActive: true, weight: 1, footballAxis: "Passing", version: 3, referenced: true }] });
    render(<CriteriaList />); expect(await screen.findByText("دقة التمرير")).toBeVisible(); expect(screen.getByText(/التمرير · الوزن 1/)).toBeVisible(); expect(screen.getByRole("link", { name: "تعديل" })).toBeVisible(); expect(screen.getByRole("button", { name: "إيقاف" })).toBeVisible();
  });

  it("shows a clear empty state for criteria", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => [] }); render(<CriteriaList />); expect(await screen.findByText("لا توجد معايير مطابقة.")).toBeVisible();
  });

  it("creates a criterion through the secure API flow", async () => {
    global.fetch = vi.fn().mockImplementation(async (input: RequestInfo | URL) => {
      const path = String(input);
      if (path.includes("/evaluations/options")) return { ok: true, json: async () => ({ enrollments: [], sports: [{ id: "s1", name: "كرة القدم" }] }) };
      if (path.includes("/auth/csrf")) return { ok: true, json: async () => ({ token: "csrf" }) };
      return { ok: true, json: async () => ({ id: "c1" }) };
    }) as typeof fetch;
    render(<CriterionForm />); const sport = await screen.findByLabelText("الرياضة"); fireEvent.change(sport, { target: { value: "s1" } }); fireEvent.change(screen.getByLabelText("الاسم بالعربية"), { target: { value: "دقة التمرير" } }); fireEvent.click(screen.getByRole("button", { name: "حفظ المعيار" })); await waitFor(() => expect(push).toHaveBeenCalledWith("/dashboard/evaluations/criteria"));
  });

  it("renders draft evaluation action and completeness", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => [{ id: "e1", player: "عمر أحمد محمود", sport: "كرة القدم", group: "مجموعة أ", evaluationDate: "2026-09-28", evaluator: "كريم حسن", status: "Draft", scoredCriteria: 2, totalApplicableCriteria: 18, completenessPercentage: 11.1, version: 1 }] });
    render(<EvaluationsList />); expect(await screen.findByText(/عمر أحمد محمود/)).toBeVisible(); expect(screen.getByText("2/18")).toBeVisible(); expect(screen.getByRole("columnheader", { name: "الاكتمال" })).toBeVisible(); expect(screen.getByRole("link", { name: "عرض وتعديل" })).toHaveAttribute("href", "/dashboard/evaluations/e1/edit");
  });

  it("uses integer 0–100 inputs and explicit Draft/Publish actions", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ id: "e1", player: "عمر أحمد محمود", sport: "كرة القدم", group: "مجموعة أ", evaluationDate: "2026-09-28", reportingPeriod: "سبتمبر", status: "Draft", version: 1, scores: [{ criterionId: "c1", name: "دقة التمرير", score: null, axis: "Passing" }] }) });
    render(<EvaluationEditor id="e1" />); const input = await screen.findByRole("spinbutton", { name: "درجة دقة التمرير" }); expect(input).toHaveAttribute("min", "0"); expect(input).toHaveAttribute("max", "100"); expect(screen.getByRole("button", { name: "حفظ المسودة" })).toBeVisible(); fireEvent.click(screen.getByRole("button", { name: "نشر التقييم" })); expect(screen.getByRole("dialog")).toHaveTextContent("سيظهر التقييم المنشور لولي الأمر");
  });

  it("shows a real six-axis football radar plus accessible text and no pitch diagram", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => footballReport }); render(<EvaluationReport id="evaluation-1" />); expect(await screen.findByRole("img", { name: "مخطط محاور أداء كرة القدم" })).toBeVisible(); expect(screen.getByLabelText("القيم النصية للمحاور")).toHaveTextContent("التمرير82"); Object.values({ Passing: "التمرير", Dribbling: "المراوغة", Speed: "السرعة", Defending: "الدفاع", Physical: "القوة البدنية", Shooting: "التسديد" }).forEach(label => expect(screen.getAllByText(label).length).toBeGreaterThan(0)); expect(screen.queryByText(/ملعب|خريطة حرارية/)).not.toBeInTheDocument();
  });

  it("shows player measurements, detailed criteria, missing values and coach notes", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => footballReport }); render(<EvaluationReport id="evaluation-1" />); expect(await screen.findByText("10 سنوات")).toBeVisible(); expect(screen.getByRole("img", { name: "صورة عمر أحمد محمود" })).toBeVisible(); expect(screen.getByText("142 سم")).toBeVisible(); expect(screen.getByText("38 كجم")).toBeVisible(); expect(screen.getByText("اليمنى")).toBeVisible(); expect(screen.getByText("84 / 100")).toBeVisible(); expect(screen.getByText("لم يُقيّم")).toBeVisible(); expect(screen.getByText("تطور ملحوظ")).toBeVisible();
  });

  it("renders a swimming report without football radar", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ ...footballReport, sport: "السباحة", footballPosition: undefined, isFootballReport: false, overallScore: undefined, availableAxes: 0, axes: [], criteria: [{ name: "تقنية التنفس", score: 78 }] }) }); render(<EvaluationReport id="swim" />); expect(await screen.findByText("الملخص الرسومي لهذه الرياضة غير مُعرّف بعد.")).toBeVisible(); expect(screen.queryByRole("img", { name: "مخطط محاور أداء كرة القدم" })).not.toBeInTheDocument(); expect(screen.getByText("تقنية التنفس")).toBeVisible();
  });

  it("shows guardian published evaluation history links", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => [{ id: "e1", sport: "كرة القدم", evaluationDate: "2026-09-28", evaluator: "كريم حسن", overallScore: 79, completenessPercentage: 100 }] }); render(<GuardianEvaluationHistory playerId="p1" />); expect(await screen.findByRole("link", { name: /عرض التقرير · 79/ })).toHaveAttribute("href", "/guardian/evaluations/e1");
  });

  it("shows an Arabic error state when a report is unavailable", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: false }); render(<EvaluationReport id="missing" guardian />); expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل التقرير أو أنه غير متاح لك.");
  });
});
