import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { EvaluationEditor, EvaluationReport, EvaluationsList, GuardianEvaluationHistory, NewEvaluation } from "./evaluations";
import { CriteriaList, CriterionForm } from "./evaluation-criteria";

const push = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push, back: vi.fn() }),
  useSearchParams: () => new URLSearchParams(window.location.search),
}));

const footballReport = {
  id: "evaluation-1", player: "عمر أحمد محمود", photoReference: "/icon.svg", footballPosition: "جناح أيمن", sport: "كرة القدم", evaluationDate: "2026-09-28", reportingPeriod: "سبتمبر 2026", evaluator: "كريم حسن", generalNotes: "تطور ملحوظ", age: 10, heightCm: 142, weightKg: 38, preferredFoot: "Right", isFootballReport: true, overallScore: 78.5, scoredCriteria: 2, totalApplicableCriteria: 3, completenessPercentage: 66.7, availableAxes: 2,
  axes: [{ axis: "Passing", value: 82 }, { axis: "Dribbling", value: 77 }, { axis: "Speed", value: undefined }, { axis: "Defending", value: undefined }, { axis: "Physical", value: undefined }, { axis: "Shooting", value: undefined }],
  criteria: [{ name: "دقة التمرير", score: 84, notes: "جيد", axis: "Passing" }, { name: "الرؤية", score: 81, axis: "Passing" }, { name: "التمركز", score: undefined }]
};

const draftRecord = {
  id: "e1", sportEnrollmentId: "en1", player: "لاعب اصطناعي", sport: "كرة القدم",
  group: "مجموعة العرض", evaluationDate: "2026-11-07", reportingPeriod: "نوفمبر",
  status: "Draft", version: 1, scores: [{ criterionId: "c1", name: "دقة التمرير", score: null, axis: "Passing" }],
};

beforeEach(() => { push.mockReset(); window.history.replaceState(null, "", "/"); });
afterEach(cleanup);

describe("evaluation experience", () => {
  it("shows recorded zero in the family report and keeps partial axes in text",async()=>{
    global.fetch=vi.fn().mockResolvedValue({ok:true,json:async()=>({...footballReport,id:"partial",performanceMean:{value:0,scoredCriteria:1,applicableCriteria:3,coveragePercent:33.3},axes:[{axis:"Passing",value:0},{axis:"Speed",value:null}],criteria:[{name:"دقة التمرير",score:0},{name:"الرؤية",score:null},{name:"التمركز",score:undefined},{name:"درجة غير صالحة",score:101}]})});
    render(<EvaluationReport id="partial" guardian/>);expect(await screen.findByText("0 / 100")).toBeVisible();expect(screen.getByText("دقة التمرير")).toBeVisible();expect(screen.queryByText("الرؤية")).not.toBeInTheDocument();expect(screen.queryByText("التمركز")).not.toBeInTheDocument();expect(screen.queryByText("درجة غير صالحة")).not.toBeInTheDocument();expect(screen.queryByText("غير متاح")).not.toBeInTheDocument();expect(screen.queryByText("لم يُقيّم")).not.toBeInTheDocument();expect(document.querySelector("polygon.radar-value")).toBeNull();expect(screen.getByLabelText("القيم النصية للمحاور")).toHaveTextContent("التمرير0");expect(screen.queryByRole("img",{name:"مخطط محاور أداء كرة القدم"})).not.toBeInTheDocument();
  });
  it("renders the criteria list with sport, axis, weight and functional actions", async () => {
    global.fetch = vi.fn(async input => Response.json(String(input).includes("/options")
      ? { sports: [{ id: "s1", name: "كرة القدم", supportsFootballAxis: true }] }
      : [{ id: "c1", sportId: "s1", sport: "كرة القدم", arabicName: "دقة التمرير", displayOrder: 1, isActive: true, weight: 1, footballAxis: "Passing", version: 3, referenced: true }]));
    render(<CriteriaList />); expect(await screen.findByText("دقة التمرير")).toBeVisible(); expect(screen.getByRole("cell",{name:"التمرير"})).toBeVisible(); expect(screen.getByRole("columnheader",{name:"الوزن"})).toBeVisible(); expect(screen.getAllByRole("cell",{name:"1"})).toHaveLength(2); expect(screen.getByRole("link", { name: "تعديل" })).toBeVisible(); expect(screen.getByRole("button", { name: "إيقاف" })).toBeVisible();
  });

  it("shows a clear empty state for criteria", async () => {
    global.fetch = vi.fn(async input => Response.json(String(input).includes("/options") ? { sports: [] } : []));
    render(<CriteriaList />); expect(await screen.findByText("لا توجد معايير تقييم بعد.")).toBeVisible();
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

  it("keeps sports without matching rows in the filter and carries scope into a new form", async () => {
    window.history.replaceState(null, "", "/dashboard/evaluations/criteria?sportId=swim&active=false&search=تنفس");
    global.fetch = vi.fn(async input => Response.json(String(input).includes("/options")
      ? { sports: [{ id: "foot", name: "كرة القدم" }, { id: "swim", name: "السباحة" }] }
      : [{ id: "c1", sportId: "foot", sport: "كرة القدم", arabicName: "دقة التمرير", isActive: true }]));
    render(<CriteriaList />);
    expect(await screen.findByText("لا توجد معايير مطابقة للفلاتر.")).toBeVisible();
    expect(screen.getByRole("option", { name: "السباحة" })).toBeVisible();
    expect(screen.getByLabelText("الرياضة")).toHaveValue("swim");
    const url = new URL(screen.getByRole("link", { name: "إضافة معيار" }).getAttribute("href")!, window.location.origin);
    expect(Object.fromEntries(url.searchParams)).toEqual({ sportId: "swim", active: "false", search: "تنفس" });
  });

  it("confirms deactivation before sending the saved version and reads the changed status", async () => {
    const writes: unknown[] = [];
    global.fetch = vi.fn(async (input, init) => {
      const path = String(input);
      if (path.includes("/options")) return Response.json({ sports: [{ id: "swim", name: "السباحة" }] });
      if (path.includes("/csrf")) return Response.json({ token: "csrf" });
      if (init?.method === "PUT") {
        writes.push(JSON.parse(String(init.body)));
        return Response.json({ version: 4 });
      }
      return Response.json([{ id: "c1", sportId: "swim", sport: "السباحة", arabicName: "التنفس",
        isActive: writes.length === 0, displayOrder: 1, weight: 1, version: writes.length ? 4 : 3 }]);
    });
    render(<CriteriaList />);
    fireEvent.click(await screen.findByRole("button", { name: "إيقاف" }));
    expect(screen.getByRole("dialog")).toHaveTextContent("لن يُضاف إلى التقييمات الجديدة");
    fireEvent.click(screen.getByRole("button", { name: "إلغاء" }));
    expect(writes).toEqual([]);
    fireEvent.click(screen.getByRole("button", { name: "إيقاف" }));
    fireEvent.click(screen.getByRole("button", { name: "تأكيد الإيقاف" }));
    expect(await screen.findByRole("button", { name: "تفعيل" })).toBeVisible();
    expect(writes).toEqual([{ isActive: false, version: 3 }]);
  });

  it("clears a football axis when changing to swimming and verifies a lost save by reading", async () => {
    const writes: unknown[] = [];
    global.fetch = vi.fn(async (input, init) => {
      const path = String(input);
      if (path.includes("/options")) return Response.json({ sports: [
        { id: "football", name: "قدم الناشئين", supportsFootballAxis: true },
        { id: "swim", name: "السباحة", supportsFootballAxis: false },
      ] });
      if (path.includes("/csrf")) return Response.json({ token: "csrf" });
      if (init?.method === "POST") {
        writes.push(JSON.parse(String(init.body)));
        throw new TypeError("Connection closed");
      }
      return Response.json([{ ...(writes[0] as object), id: "c1" }]);
    });
    render(<CriterionForm />);
    const sport = await screen.findByLabelText("الرياضة");
    fireEvent.change(sport, { target: { value: "football" } });
    fireEvent.change(screen.getByLabelText("محور كرة القدم"), { target: { value: "Passing" } });
    fireEvent.change(sport, { target: { value: "swim" } });
    expect(screen.queryByLabelText("محور كرة القدم")).not.toBeInTheDocument();
    fireEvent.change(screen.getByLabelText("الاسم بالعربية"), { target: { value: "تقنية التنفس" } });
    fireEvent.change(screen.getByLabelText("الوزن"), { target: { value: "١٫٢٥" } });
    fireEvent.change(screen.getByLabelText("الترتيب"), { target: { value: "٢" } });
    fireEvent.click(screen.getByRole("button", { name: "حفظ المعيار" }));
    const verify = await screen.findByRole("button", { name: "التحقق من الحفظ" });
    expect(screen.queryByRole("button", { name: "حفظ المعيار" })).not.toBeInTheDocument();
    fireEvent.click(verify);
    await waitFor(() => expect(push).toHaveBeenCalledWith("/dashboard/evaluations/criteria"));
    expect(writes).toEqual([expect.objectContaining({ sportId: "swim", footballAxis: null, weight: 1.25, displayOrder: 2 })]);
  });

  it("shows a missing criterion as unavailable and retains the return link", async () => {
    global.fetch = vi.fn(async input => Response.json(String(input).includes("/options") ? { sports: [] } : []));
    render(<CriterionForm id="missing" />);
    expect(await screen.findByRole("alert")).toHaveTextContent("هذا المعيار غير متاح.");
    expect(screen.getByRole("link", { name: "العودة إلى معايير التقييم" })).toHaveAttribute("href", "/dashboard/evaluations/criteria");
  });

  it("renders draft evaluation action and completeness", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => [{ id: "e1", player: "عمر أحمد محمود", sport: "كرة القدم", group: "مجموعة أ", evaluationDate: "2026-09-28", evaluator: "كريم حسن", status: "Draft", scoredCriteria: 2, totalApplicableCriteria: 18, completenessPercentage: 11.1, version: 1 }] });
    render(<EvaluationsList />); expect(await screen.findByText(/عمر أحمد محمود/)).toBeVisible(); expect(screen.getByText("2/18")).toBeVisible(); expect(screen.getByRole("columnheader", { name: "الاكتمال" })).toBeVisible(); expect(screen.getByRole("link", { name: "عرض وتعديل" })).toHaveAttribute("href", "/dashboard/evaluations/e1/edit");
  });

  it("requests only the selected player's evaluations when opened from the player profile", async () => {
    const playerId = "11223344-5566-4789-8012-112233445566";
    window.history.replaceState(null, "", `/dashboard/evaluations?playerId=${playerId}`);
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => [] });
    render(<EvaluationsList />);
    await screen.findByText("لا توجد تقييمات");
    expect(vi.mocked(fetch).mock.calls.some(([input]) => String(input).includes(`playerId=${playerId}`))).toBe(true);
  });

  it("preserves the published-list search and page on report links", async () => {
    window.history.replaceState(null, "", "/dashboard/evaluations/reports?search=abc&page=2&pageSize=10&status=Draft");
    global.fetch = vi.fn().mockResolvedValue(Response.json(Array.from({ length: 11 }, (_, i) => ({
      ...draftRecord, id: `r${i}`, player: `لاعب العرض ${i}`, status: "Published", scoredCriteria: 1, totalApplicableCriteria: 1,
    }))));
    render(<EvaluationsList publishedOnly />);
    const link = await screen.findByRole("link", { name: "عرض التقرير" });
    expect(link).toHaveAttribute("href", "/dashboard/evaluations/r10/report?search=abc&page=2&pageSize=10&from=published");
    expect(link).toHaveAttribute("id", "evaluation-r10");
    expect(fetch).toHaveBeenCalledWith("/api/v1/evaluations?search=abc&status=Published", expect.anything());
  });

  it("clears results when the evaluation scope changes and retries only the current scope", async () => {
    global.fetch = vi.fn()
      .mockResolvedValueOnce(Response.json([{ ...draftRecord, scoredCriteria: 0, totalApplicableCriteria: 1 }]))
      .mockResolvedValueOnce(Response.json({}, { status: 503 }))
      .mockResolvedValueOnce(Response.json([]));
    const view = render(<EvaluationsList />);
    await screen.findByText("لاعب اصطناعي");

    window.history.replaceState(null, "", "/dashboard/evaluations?status=PublishedInternal");
    view.rerender(<EvaluationsList />);
    expect(screen.queryByText("لاعب اصطناعي")).not.toBeInTheDocument();
    fireEvent.click(await screen.findByRole("button", { name: "إعادة المحاولة" }));

    expect(await screen.findByText("لا توجد نتائج تطابق الفلاتر الحالية.")).toBeVisible();
    expect(vi.mocked(fetch).mock.calls.map(([path]) => path)).toEqual([
      "/api/v1/evaluations?", "/api/v1/evaluations?status=PublishedInternal", "/api/v1/evaluations?status=PublishedInternal",
    ]);
  });

  it("validates scores and separates internal approval from publication", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => draftRecord });
    render(<EvaluationEditor id="e1" />);
    const input = await screen.findByRole("textbox", { name: "درجة دقة التمرير" });
    expect(input).toHaveAttribute("inputmode", "numeric");
    expect(screen.getByRole("button", { name: "حفظ المسودة" })).toBeVisible();
    expect(screen.getByRole("button", { name: "نشر لولي الأمر" })).toBeDisabled();
    fireEvent.change(input, { target: { value: "١٠١" } });
    expect(screen.getByText("أدخل عددًا صحيحًا من 0 إلى 100.")).toBeVisible();
    expect(screen.getByRole("button", { name: "حفظ المسودة" })).toBeDisabled();
    fireEvent.change(input, { target: { value: "٠" } });
    fireEvent.click(screen.getByRole("button", { name: "اعتماد داخلي" }));
    expect(screen.getByRole("dialog")).toHaveTextContent("متاحًا لفريق العمل فقط");
  });

  it("starts the selected player's draft on the academy date and freezes an uncertain create", async () => {
    const writes: unknown[] = [];
    global.fetch = vi.fn(async (input, init) => {
      const path = String(input);
      if (path.endsWith("/calendar")) return Response.json({ today: "2026-11-07" });
      if (path.endsWith("/options")) return Response.json({
        canEvaluate: true, sports: [{ id: "s1", name: "كرة القدم" }],
        enrollments: [{ id: "en1", playerId: "p1", player: "لاعب اصطناعي", playerCode: "TEST01", sportId: "s1", sport: "كرة القدم", groupId: "g1", group: "مجموعة العرض", criteriaReady: true }],
      });
      if (path.endsWith("/csrf")) return Response.json({ token: "csrf" });
      writes.push(JSON.parse(String(init?.body)));
      throw Error("connection lost");
    });
    render(<NewEvaluation initialEnrollment="EN1" />);
    expect(await screen.findByText("لاعب اصطناعي")).toBeVisible();
    expect(screen.getByLabelText("تاريخ التقييم")).toHaveValue("2026-11-07");
    expect(screen.getByLabelText("فترة التقرير")).toHaveValue("نوفمبر 2026");
    fireEvent.change(screen.getByLabelText("تاريخ التقييم"), { target: { value: "2026-10-31" } });
    expect(screen.getByLabelText("فترة التقرير")).toHaveValue("أكتوبر 2026");
    fireEvent.click(screen.getByRole("button", { name: "إنشاء المسودة" }));
    const recovery = await screen.findByRole("link", { name: "عرض تقييمات اللاعب" });
    expect(recovery).toHaveAttribute("href", "/dashboard/evaluations?playerId=p1");
    expect(screen.queryByRole("button", { name: "إنشاء المسودة" })).not.toBeInTheDocument();
    expect(writes).toEqual([{ sportEnrollmentId: "en1", evaluationDate: "2026-10-31", reportingPeriod: "أكتوبر 2026", generalNotes: null }]);
  });

  it("blocks publishing a concurrent draft and rechecks with GET only", async () => {
    const writes: string[] = [];
    let score = 42;
    global.fetch = vi.fn(async (input, init) => {
      const path = String(input);
      if (path.endsWith("/csrf")) return Response.json({ token: "csrf" });
      if (init?.method) { writes.push(init.method); return Response.json({ id: "e1", version: 2 }); }
      return Response.json(writes.length ? { ...draftRecord, version: 2, scores: [{ ...draftRecord.scores[0], score }] } : draftRecord);
    });
    render(<EvaluationEditor id="e1" />);
    fireEvent.change(await screen.findByRole("textbox", { name: "درجة دقة التمرير" }), { target: { value: "٦٨" } });
    fireEvent.click(screen.getByRole("button", { name: "نشر لولي الأمر" }));
    fireEvent.click(screen.getByRole("button", { name: "تأكيد النشر" }));
    await screen.findByText("لم تتطابق النسخة المحفوظة مع التغييرات المطلوبة.");
    expect(writes).toEqual(["PUT"]);
    score = 68;
    fireEvent.click(screen.getByRole("button", { name: "التحقق من التقييم" }));
    await screen.findByText("تم حفظ المسودة.");
    expect(writes).toEqual(["PUT"]);
    expect(push).not.toHaveBeenCalled();
  });

  it("checks an uncertain internal publication without replaying the POST", async () => {
    const writes: unknown[] = [];
    let status = "Draft";
    global.fetch = vi.fn(async (input, init) => {
      const path = String(input);
      if (path.endsWith("/csrf")) return Response.json({ token: "csrf" });
      if (init?.method) {
        writes.push(JSON.parse(String(init.body)));
        status = "PublishedInternal";
        throw Error("connection lost");
      }
      return Response.json({ ...draftRecord, status, scores: [{ ...draftRecord.scores[0], score: 0 }] });
    });
    render(<EvaluationEditor id="e1" />);
    fireEvent.click(await screen.findByRole("button", { name: "اعتماد داخلي" }));
    fireEvent.click(screen.getByRole("button", { name: "تأكيد الاعتماد" }));
    const check = await screen.findByRole("button", { name: "التحقق من التقييم" });
    await waitFor(() => expect(check).toHaveFocus());
    expect(push).not.toHaveBeenCalled();
    fireEvent.click(check);
    await waitFor(() => expect(push).toHaveBeenCalledWith("/dashboard/evaluations/e1/report"));
    expect(writes).toEqual([{ version: 1, shareWithGuardian: false }]);
  });

  it("shows a real six-axis football radar plus accessible text and no pitch diagram", async () => {
    const axes = footballReport.axes.map(item => ({ ...item, value: item.value ?? 70 }));
    global.fetch = vi.fn().mockResolvedValue(Response.json({ ...footballReport, axes }));
    render(<EvaluationReport id="evaluation-1" />);
    expect(await screen.findByRole("img", { name: "مخطط محاور أداء كرة القدم" })).toBeVisible();
    expect(screen.getByLabelText("القيم النصية للمحاور")).toHaveTextContent("التمرير82");
    expect(screen.getByLabelText("القيم النصية للمحاور").querySelectorAll("dt")).toHaveLength(6);
  });

  it("shows player measurements, detailed criteria, missing values and coach notes", async () => {
    global.fetch = vi.fn().mockResolvedValue(Response.json(footballReport));
    render(<EvaluationReport id="evaluation-1" />);
    expect(await screen.findByText("10 سنوات")).toBeVisible();
    expect(screen.getByRole("img", { name: "صورة عمر أحمد محمود" })).toBeVisible();
    expect(screen.getByText("142 سم")).toBeVisible();
    expect(screen.getByText("38 كجم")).toBeVisible();
    expect(screen.getByText("اليمنى")).toBeVisible();
    expect(screen.getByText("84 / 100")).toBeVisible();
    expect(screen.getByText("لم يُقيّم")).toBeVisible();
    expect(screen.getByText("تطور ملحوظ")).toBeVisible();
    expect(screen.getByLabelText("القيم النصية للمحاور")).toHaveTextContent("التمرير82");
    expect(screen.queryByRole("img", { name: "مخطط محاور أداء كرة القدم" })).not.toBeInTheDocument();
  });

  it("renders a swimming report without football radar", async () => {
    global.fetch = vi.fn().mockResolvedValue(Response.json({
      ...footballReport, id: "swim", sport: "السباحة", isFootballReport: false, overallScore: null,
      performanceMean: { value: 0, scoredCriteria: 1, applicableCriteria: 1 }, criteria: [{ name: "تقنية التنفس", score: 0 }],
    }));
    render(<EvaluationReport id="swim" />);
    expect(await screen.findByText("تقنية التنفس")).toBeVisible();
    expect(screen.getByRole("region", { name: "ملخص التقييم" })).toHaveTextContent("متوسط الأداء0 من 100");
    expect(screen.getByText("0 / 100")).toBeVisible();
    expect(screen.queryByText("القدم المفضلة")).not.toBeInTheDocument();
    expect(screen.queryByText(/جناح أيمن|الملخص الرسومي/)).not.toBeInTheDocument();
    expect(screen.queryByRole("img", { name: "مخطط محاور أداء كرة القدم" })).not.toBeInTheDocument();
  });

  it("keeps football details out of a family swimming report and links back to the player", async () => {
    global.fetch = vi.fn().mockResolvedValue(Response.json({
      ...footballReport, id: "swim", sport: "السباحة", isFootballReport: false, axes: [],
      criteria: [{ name: "تقنية التنفس", score: 78 }], generalNotes: "",
    }));
    render(<EvaluationReport id="swim" guardian childId="p1" />);
    expect(await screen.findByText("تقنية التنفس")).toBeVisible();
    expect(screen.getByText("السباحة")).toBeVisible();
    expect(screen.queryByText("جناح أيمن")).not.toBeInTheDocument();
    expect(screen.queryByText("القدم المفضلة")).not.toBeInTheDocument();
    expect(screen.queryByText("ملاحظات المدرب")).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "العودة إلى ملف اللاعب" })).toHaveAttribute("href", "/guardian/children/p1#evaluations");
  });

  it("retries a report load and removes the prior report when a different one is denied", async () => {
    global.fetch = vi.fn()
      .mockResolvedValueOnce(Response.json({}, { status: 503 }))
      .mockResolvedValueOnce(Response.json(footballReport))
      .mockResolvedValueOnce(Response.json({}, { status: 404 }));
    const view = render(<EvaluationReport id="evaluation-1" guardian />);
    fireEvent.click(await screen.findByRole("button", { name: "إعادة المحاولة" }));
    expect(await screen.findByRole("heading", { name: footballReport.player })).toBeVisible();
    view.rerender(<EvaluationReport id="other" guardian />);
    expect(screen.queryByRole("heading", { name: footballReport.player })).not.toBeInTheDocument();
    expect(await screen.findByRole("alert")).toHaveTextContent("هذا التقرير غير متاح لحسابك.");
    expect(screen.queryByRole("button", { name: "إعادة المحاولة" })).not.toBeInTheDocument();
  });

  it("shows guardian published evaluation history links", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => [{ id: "e1", sport: "كرة القدم", evaluationDate: "2026-09-28", evaluator: "كريم حسن", overallScore: 79, completenessPercentage: 100 }] }); render(<GuardianEvaluationHistory playerId="p1" />); expect(await screen.findByRole("link", { name: /عرض التقرير · 79/ })).toHaveAttribute("href", "/guardian/evaluations/e1");
  });

  it("shows an Arabic error state when a report is unavailable", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: false }); render(<EvaluationReport id="missing" guardian />); expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل التقرير أو أنه غير متاح لك.");
  });
});
