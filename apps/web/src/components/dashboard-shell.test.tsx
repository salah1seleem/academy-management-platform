import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { AdminDataTable, ConfirmationDialog, DashboardShell, FilterToolbar, PageHeader, RowActions, SearchInput, StatusBadge } from "./dashboard-shell";
import { GuardianShell } from "./guardian-shell";

const replace = vi.fn(), push = vi.fn();
const router = { replace, push };
let currentPath = "/dashboard/players";
vi.mock("next/navigation", () => ({ usePathname: () => currentPath, useRouter: () => router }));

beforeEach(() => { currentPath = "/dashboard/players"; replace.mockReset(); global.fetch = vi.fn(input => Promise.resolve(new Response(JSON.stringify(String(input).includes("my-permissions") ? ["people.manage", "structure.manage", "payment.read", "subscription.plan.manage", "evaluation.read", "evaluation.criteria.manage"] : { displayName: "منى السيد", role: "AcademyAdmin", academyName: "أكاديمية النجوم" }), { status: 200 }))) as typeof fetch; });
afterEach(cleanup);

describe("dashboard navigation governance", () => {
  it.each([
    { role: "AcademyOwner", features: ["training", "facilities"], permissions: ["structure.manage"], visible: false },
    { role: "AcademyAdmin", features: ["training", "facilities"], permissions: ["structure.manage"], visible: false },
    { role: "AcademyOwner", features: ["training", "matches"], permissions: [], visible: true },
    { role: "AcademyAdmin", features: ["training", "matches"], permissions: ["structure.manage"], visible: true },
    { role: "AcademyAdmin", features: ["training", "matches"], permissions: [], visible: false },
  ])("uses the academy matches feature and $role permissions for sidebar and quick jump ($visible)", async ({ role, features, permissions, visible }) => {
    global.fetch = vi.fn(async input => Response.json(
      String(input).includes("my-permissions") ? permissions :
      String(input).includes("academy/experience") ? { displayName: "أكاديمية الاختبار", onboarding: { features } } :
      { displayName: "موظف اختبار", role }
    ));
    render(<DashboardShell><div>المحتوى</div></DashboardShell>);
    await screen.findByText("أكاديمية الاختبار");
    const sidebar = screen.getByRole("complementary", { name: "التنقل الرئيسي" });
    const jump = screen.getByRole("combobox", { name: "انتقل مباشرة إلى" });
    expect(Boolean(within(sidebar).queryByRole("link", { name: "البطولات والمباريات" }))).toBe(visible);
    expect(Boolean(within(jump).queryByRole("option", { name: "البطولات والمباريات" }))).toBe(visible);
  });

  it("identifies personal account screens and does not mark Home as the active destination", async () => {
    currentPath="/dashboard/account/attendance";
    render(<DashboardShell><PageHeader title="حضوري وانصرافي" context="الحساب"/></DashboardShell>);
    await screen.findAllByText("منى السيد");
    const sidebar=screen.getByRole("complementary",{name:"التنقل الرئيسي"});
    expect(within(sidebar).getByRole("link",{name:"الرئيسية"})).not.toHaveAttribute("aria-current");
    expect(within(sidebar).getByRole("link",{name:"حضوري الوظيفي"})).toHaveAttribute("aria-current","page");
    expect(sidebar.querySelector(".sidebar-security-link")).toHaveAttribute("open");
  });
  it("keeps optional page guidance behind an explicit disclosure while errors remain visible", () => {
    render(<><PageHeader title="مراجعة ملف" context="الأعضاء" description="شرح اختياري للصفحة"/><p role="alert">راجع القيمة قبل الاعتماد</p></>);
    expect(screen.getByText("شرح اختياري للصفحة")).not.toBeVisible();
    fireEvent.click(screen.getByText("إرشادات الصفحة"));
    expect(screen.getByText("شرح اختياري للصفحة")).toBeVisible();
    expect(screen.getByRole("alert")).toBeVisible();
  });
  it("offers only permitted destinations in direct navigation and follows the chosen route", async () => {
    render(<DashboardShell><PageHeader title="اللاعبون" context="الأعضاء"/></DashboardShell>);
    await screen.findAllByText("منى السيد");
    const jump=screen.getByRole("combobox",{name:"انتقل مباشرة إلى"});
    expect(within(jump).queryByRole("option",{name:"الحسابات والماليات"})).not.toBeInTheDocument();
    expect(within(jump).queryByRole("option",{name:"إيصالات الاشتراكات"})).not.toBeInTheDocument();
    expect(within(jump).queryByRole("option",{name:"الصلاحيات"})).not.toBeInTheDocument();
    expect(within(jump).getByRole("option",{name:"اللاعبون"})).toHaveValue("/dashboard/players");
    fireEvent.change(jump,{target:{value:"/dashboard/guardians"}});
    expect(push).toHaveBeenCalledWith("/dashboard/guardians");
    expect(screen.getByRole("link",{name:"العودة إلى الرئيسية"})).toHaveAttribute("href","/dashboard");
  });

  it.each(["/dashboard/reports/financial", "/dashboard/reports/receipts"])("keeps %s inside its finance workspace", async path => {
    currentPath=path;
    global.fetch=vi.fn(async()=>Response.json({displayName:"مالك عرض",role:"AcademyOwner"}));
    render(<DashboardShell><div>تقرير التحصيل</div></DashboardShell>);
    const rail=screen.getByRole("complementary",{name:"التنقل الرئيسي"});
    expect(rail).toHaveAttribute("data-workspace","finance");
    await screen.findAllByText("مالك عرض");
    expect(within(rail).getByRole("link",{name:"الرئيسية"})).not.toHaveAttribute("aria-current");
    expect(within(rail).getByRole("link",{name:"إيصالات الاشتراكات"})).toHaveAttribute("href","/dashboard/reports/receipts");
  });
  it("keeps the shared inbox discoverable without granting template management", async () => {
    currentPath="/dashboard/notifications";
    render(<DashboardShell><div>الوارد المصرح به</div></DashboardShell>);
    expect(await screen.findByRole("link",{name:"التنبيهات"})).toHaveAttribute("href","/dashboard/notifications");
    expect(screen.getByRole("link",{name:"التنبيهات"})).toHaveAttribute("aria-current","page");
  });

  it("shows the published activity feed without granting publishing access", async () => {
    currentPath = "/dashboard/activities";
    global.fetch = vi.fn(async input => Response.json(
      String(input).includes("my-permissions") ? [] :
      String(input).includes("academy/experience") ? { displayName: "أكاديمية الاختبار", onboarding: { features: ["news"] } } :
      { displayName: "موظف اختبار", role: "AcademyAdmin" }
    ));
    render(<DashboardShell><div>محتوى الأنشطة</div></DashboardShell>);
    const sidebar = screen.getByRole("complementary", { name: "التنقل الرئيسي" });
    const feed = await within(sidebar).findByRole("link", { name: "الأخبار والفعاليات" });
    expect(feed).toHaveAttribute("href", "/dashboard/activities");
    expect(feed).toHaveAttribute("aria-current", "page");
    expect(within(sidebar).queryByRole("link", { name: "إدارة النشر" })).not.toBeInTheDocument();
    expect(sidebar).toHaveAttribute("data-workspace", "news");
  });

  it("highlights the authorized help entry rather than Home and closes the mobile drawer", async () => {
    currentPath = "/dashboard/help/packages";
    global.fetch = vi.fn(async input => Response.json(String(input).includes("help/availability") ? { available: true } : String(input).includes("my-permissions") ? [] : { displayName: "مالك اختبار", role: "AcademyOwner" }));
    render(<DashboardShell><div>دليل الباقة</div></DashboardShell>);
    const link = await screen.findByRole("link", { name: "دليل الاستخدام" });
    expect(link).toHaveAttribute("aria-current", "page");
    expect(within(screen.getByRole("complementary", {name:"التنقل الرئيسي"})).getByRole("link", { name: "الرئيسية" })).not.toHaveAttribute("aria-current");
    fireEvent.click(screen.getByRole("button", { name: "فتح القائمة" }));
    link.addEventListener("click", e => e.preventDefault()); fireEvent.click(link);
    expect(screen.getByRole("button", { name: "فتح القائمة" })).toBeVisible();
  });
  it("auto-expands the current module, highlights its list route, and supports accordion toggling", async () => {
    render(<DashboardShell><div>المحتوى</div></DashboardShell>);
    const players = await screen.findByRole("button", { name: "الأعضاء والاشتراكات" });
    expect(players).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("link", { name: "اللاعبون" })).toHaveAttribute("aria-current", "page");
    expect(screen.queryByRole("link", { name: "الأعضاء والاشتراكات" })).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "عمليات الأكاديمية" })).toHaveAttribute("aria-expanded", "false");
    fireEvent.click(players);
    expect(players).toHaveAttribute("aria-expanded", "false");
    fireEvent.click(screen.getByRole("button", { name: "عمليات الأكاديمية" }));
    expect(screen.getByRole("button", { name: "عمليات الأكاديمية" })).toHaveAttribute("aria-expanded", "true");
  });

  it("opens a lower module without collapsing the section above it or reordering headings", async () => {
    global.fetch = vi.fn(async () => Response.json({ displayName: "مالك تمرير", role: "AcademyOwner" }));
    render(<DashboardShell><div>المحتوى</div></DashboardShell>);
    await screen.findAllByText("مالك تمرير");
    const players = await screen.findByRole("button", { name: "الأعضاء والاشتراكات" });
    const followups = screen.getByRole("button", { name: "المتابعات والطلبات" });
    const before = screen.getAllByRole("button").map(button => button.textContent);
    fireEvent.click(followups);
    expect(players).toHaveAttribute("aria-expanded", "true");
    expect(followups).toHaveAttribute("aria-expanded", "true");
    expect(screen.getAllByRole("button").map(button => button.textContent)).toEqual(before);
    fireEvent.click(followups);
    expect(players).toHaveAttribute("aria-expanded", "true");
    expect(followups).toHaveAttribute("aria-expanded", "false");
  });

  it("removes renewal opportunities from academic operations and the quick jump", async () => {
    global.fetch = vi.fn(async () => Response.json({ displayName: "مالك اختبار", role: "AcademyOwner" }));
    render(<DashboardShell><div>المحتوى</div></DashboardShell>);
    await screen.findAllByText("مالك اختبار");
    fireEvent.click(screen.getByRole("button", { name: "عمليات الأكاديمية" }));
    expect(screen.queryByText("فرص التجديد")).not.toBeInTheDocument();
    expect(screen.getByRole("link", { name: "تجديد اشتراك" })).toHaveAttribute("href", "/dashboard/subscriptions/renew/new");
  });

  it("keeps the create action visible and row actions execute real links or callbacks", () => {
    const callback = vi.fn();
    render(<><PageHeader title="قائمة اللاعبين" context="اللاعبون / قائمة اللاعبين" action={{ label: "تسجيل لاعب جديد", href: "/dashboard/players/new" }} /><RowActions actions={[{ label: "عرض", href: "/dashboard/players/1" }, { label: "إيقاف", onClick: callback }]} /></>);
    expect(screen.getByRole("link", { name: "تسجيل لاعب جديد" })).toHaveAttribute("href", "/dashboard/players/new");
    expect(screen.getByRole("link", { name: "عرض" })).toHaveAttribute("href", "/dashboard/players/1");
    fireEvent.click(screen.getByRole("button", { name: "إيقاف" }));
    expect(callback).toHaveBeenCalledOnce();
  });

  it("contains keyboard navigation in the mobile drawer and restores focus on Escape", async () => {
    const { container } = render(<DashboardShell><button>إجراء في الصفحة</button></DashboardShell>);
    await screen.findAllByText("منى السيد");
    const toggle = screen.getByRole("button", { name: "فتح القائمة" });
    const skip = screen.getByRole("link", { name: "انتقل إلى محتوى الصفحة" });
    fireEvent.click(toggle);
    expect(toggle).toHaveAttribute("aria-expanded", "true");
    const sidebar = screen.getByRole("complementary", { name: "التنقل الرئيسي" });
    expect(sidebar).toHaveClass("open");
    expect(sidebar).toHaveFocus();
    expect(container.querySelector("main")).toHaveAttribute("inert");
    expect(skip).toHaveAttribute("inert");
    fireEvent.keyDown(sidebar, { key: "Tab", shiftKey: true });
    expect(toggle).toHaveFocus();
    fireEvent.keyDown(toggle, { key: "Tab" });
    expect(sidebar.contains(document.activeElement)).toBe(true);
    fireEvent.keyDown(document.activeElement!, { key: "Escape" });
    expect(toggle).toHaveAttribute("aria-expanded", "false");
    expect(toggle).toHaveFocus();
    expect(container.querySelector("main")).not.toHaveAttribute("inert");
    expect(skip).not.toHaveAttribute("inert");
  });

  it("stays on the dashboard when logout fails and only redirects after retry succeeds", async () => {
    let attempt = 0;
    global.fetch = vi.fn(async input => {
      const path = String(input);
      if (path.endsWith("/csrf")) return Response.json({ token: "demo" });
      if (path.endsWith("/logout")) return new Response(null, { status: ++attempt === 1 ? 503 : 204 });
      return Response.json({ displayName: "موظف اختبار", role: "AcademyOwner" });
    });
    render(<DashboardShell><div>المحتوى</div></DashboardShell>);
    await screen.findAllByText("موظف اختبار");
    const logout = screen.getByRole("button", { name: "تسجيل الخروج" });
    fireEvent.click(logout);
    expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تسجيل الخروج. حاول مرة أخرى.");
    expect(replace).not.toHaveBeenCalled();
    fireEvent.click(logout);
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/login"));
  });

  it("identifies the guardian section on receipt detail without marking Home as current", () => {
    currentPath = "/guardian/receipts/receipt-1";
    render(<GuardianShell><h1>الإيصال</h1></GuardianShell>);
    expect(screen.getByRole("link", { name: "الإيصالات" })).toHaveAttribute("aria-current", "true");
    expect(screen.getByRole("link", { name: "الرئيسية" })).not.toHaveAttribute("aria-current");
  });

  it("does not repurpose a read link when write permissions finish loading", () => {
    const due = { label: "المستحقات", href: "/dashboard/payroll?employeeId=1" };
    const { rerender } = render(<RowActions actions={[due]} />);
    const link = screen.getByRole("link", { name: "المستحقات" });
    rerender(<RowActions actions={[{ label: "تعديل", href: "/dashboard/staff/employees/1" }, due]} />);
    expect(screen.getByRole("link", { name: "المستحقات" })).toBe(link);
    expect(link).toHaveAttribute("href", due.href);
    expect(screen.getByRole("link", { name: "تعديل" })).not.toBe(link);
  });

  it("relocates plans to setup and payments to followups without losing canonical routes", async () => {
    currentPath = "/dashboard/subscriptions/plans";
    render(<DashboardShell><div>المحتوى</div></DashboardShell>);
    expect(await screen.findByRole("link", { name: "باقات الاشتراك" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("link", {name:"المتابعات والطلبات"})).toHaveAttribute("href","/dashboard/subscriptions/payments");
    expect(screen.getByRole("link", {name:"المتابعات والطلبات"})).toBeVisible();
    expect(screen.queryByRole("button", { name: "الاشتراكات" })).not.toBeInTheDocument();
    expect(screen.queryByText("نظرة عامة")).not.toBeInTheDocument();
    expect(screen.queryByText("الإدارة")).not.toBeInTheDocument();
  });

  it("shows the evaluation module with criteria and players, without the published reports sub-tab", async () => {
    currentPath = "/dashboard/evaluations";
    render(<DashboardShell><div>المحتوى</div></DashboardShell>);
    const evaluationModule = await screen.findByRole("button", { name: "الأعضاء والاشتراكات" });
    expect(evaluationModule).toHaveAttribute("aria-expanded", "true");
    expect(screen.getByRole("link", { name: "معايير تقييم اللاعبين", hidden: true })).toHaveAttribute("href", "/dashboard/evaluations/criteria");
    expect(screen.getByRole("link", { name: "تقييمات اللاعبين" })).toHaveAttribute("aria-current", "page");
    expect(screen.queryByRole("link", { name: "التقارير المنشورة" })).not.toBeInTheDocument();
  });

  it("hides financial reports from administrators without an owner grant", async () => {
    currentPath = "/dashboard/reports/financial";
    render(<DashboardShell><div>المحتوى</div></DashboardShell>);
    await screen.findAllByText("منى السيد");
    expect(screen.queryByRole("link", { name: "التقارير المالية" })).not.toBeInTheDocument();
    expect(screen.queryByRole("link", { name: "الإيصالات" })).not.toBeInTheDocument();
  });

  it("redirects coaches away from the web dashboard", async () => {
    currentPath = "/dashboard/reports/attendance";
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => ({ displayName: "المدرب", role: "Coach" }) });
    render(<DashboardShell><div>المحتوى</div></DashboardShell>);
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/"));
  });

  it("keeps secondary row actions in an accessible overflow menu", () => {
    const callback = vi.fn();
    render(<RowActions actions={[{ label: "عرض", href: "/record/1" }, { label: "تعديل", href: "/record/1/edit" }, { label: "إيقاف", onClick: callback, tone: "danger" }]} />);
    expect(screen.getByRole("link", { name: "عرض" })).toBeVisible();
    fireEvent.click(screen.getByLabelText("المزيد من الإجراءات"));
    expect(screen.getByRole("menuitem", { name: "تعديل" })).toHaveAttribute("href", "/record/1/edit");
    fireEvent.click(screen.getByRole("menuitem", { name: "إيقاف" }));
    expect(callback).toHaveBeenCalledOnce();
  });

  it("opens overflow actions from the keyboard and closes on Escape or an outside press", () => {
    render(<><RowActions actions={[{ label: "عرض", href: "/record/1" }, { label: "تعديل", href: "/record/1/edit" }, { label: "إيقاف", onClick: vi.fn() }]} /><button>عنصر خارج القائمة</button></>);
    const trigger = screen.getByRole("button", { name: "المزيد من الإجراءات" });
    trigger.focus();
    fireEvent.keyDown(trigger, { key: "ArrowDown" });
    const edit = screen.getByRole("menuitem", { name: "تعديل" });
    expect(edit).toHaveFocus();
    fireEvent.keyDown(edit, { key: "End" });
    const stop = screen.getByRole("menuitem", { name: "إيقاف" });
    expect(stop).toHaveFocus();
    fireEvent.keyDown(stop, { key: "Escape" });
    expect(trigger).toHaveFocus();
    expect(trigger.closest("details")).not.toHaveAttribute("open");
    fireEvent.keyDown(trigger, { key: "ArrowDown" });
    fireEvent.pointerDown(screen.getByRole("button", { name: "عنصر خارج القائمة" }));
    expect(trigger.closest("details")).not.toHaveAttribute("open");
  });

  it("keeps focus inside a confirmation and restores the invoking control on close", () => {
    const onCancel = vi.fn();
    const confirmation = { title: "تأكيد الإيقاف", message: "سيتوقف التسجيل في هذه المجموعة.", confirmLabel: "إيقاف", onCancel, onConfirm: vi.fn() };
    const { rerender } = render(<><button>بدء الإيقاف</button><ConfirmationDialog open={false} {...confirmation} /></>);
    const trigger = screen.getByRole("button", { name: "بدء الإيقاف" });
    trigger.focus();
    rerender(<><button>بدء الإيقاف</button><ConfirmationDialog open {...confirmation} /></>);
    const dialog = screen.getByRole("dialog", { name: "تأكيد الإيقاف" });
    const cancel = within(dialog).getByRole("button", { name: "إلغاء" });
    expect(cancel).toHaveFocus();
    fireEvent.keyDown(cancel, { key: "Tab" });
    expect(within(dialog).getByRole("button", { name: "إيقاف" })).toHaveFocus();
    trigger.focus();
    expect(dialog).toHaveFocus();
    fireEvent.keyDown(dialog, { key: "Escape" });
    expect(onCancel).toHaveBeenCalledOnce();
    rerender(<><button>بدء الإيقاف</button><ConfirmationDialog open={false} {...confirmation} /></>);
    expect(trigger).toHaveFocus();
  });

  it("renders Arabic status labels and a structured responsive table", () => {
    const rows = [{ id: "1", name: "عمر", status: "Active" }];
    render(<><StatusBadge status="Active" /><AdminDataTable label="قائمة تجريبية" rows={rows} rowKey={row => row.id} columns={[{ key: "name", header: "الاسم", primary: true, render: row => row.name }, { key: "status", header: "الحالة", render: row => <StatusBadge status={row.status} /> }]} /></>);
    expect(screen.getByRole("table", { name: "قائمة تجريبية" })).toBeVisible();
    expect(screen.getAllByText("فعال")).toHaveLength(2);
    expect(screen.queryByText("Active")).not.toBeInTheDocument();
  });

  it("dismisses the column picker without discarding column choices", () => {
    const rows = [{ id: "1", name: "لاعب اصطناعي", status: "فعال" }];
    render(<AdminDataTable label="قائمة تجريبية" rows={rows} rowKey={row => row.id} columns={[
      { key: "name", header: "الاسم", primary: true, render: row => row.name },
      { key: "status", header: "الحالة", render: row => row.status },
    ]} />);
    const summary = screen.getByText("الأعمدة");
    const menu = summary.closest("details");
    fireEvent.click(summary);
    const status = screen.getByRole("checkbox", { name: "إظهار عمود الحالة" });
    fireEvent.click(status);
    fireEvent.keyDown(status, { key: "Escape" });
    expect(menu).not.toHaveAttribute("open");
    expect(summary).toHaveFocus();
    fireEvent.click(summary);
    expect(status).not.toBeChecked();
    fireEvent.pointerDown(screen.getByRole("table", { name: "قائمة تجريبية" }));
    expect(menu).not.toHaveAttribute("open");
    expect(screen.queryByRole("columnheader", { name: "الحالة" })).not.toBeInTheDocument();
  });

  it("uses an Arabic fallback for unrecognized status codes", () => {
    render(<><StatusBadge status="PendingPayment" /><StatusBadge status="NewStatusCode" /></>);
    expect(screen.getByText("بانتظار الدفع")).toBeVisible();
    expect(screen.getByText("غير معروفة")).toBeVisible();
    expect(screen.queryByText("NewStatusCode")).not.toBeInTheDocument();
  });

  it("exposes a labelled filter bar with result count and reset", () => {
    const reset = vi.fn();
    render(<FilterToolbar resultCount={7} onReset={reset}><SearchInput value="" onChange={() => {}} label="بحث السجلات" /></FilterToolbar>);
    expect(screen.getByRole("textbox", { name: "بحث السجلات" })).toBeVisible();
    expect(screen.getByText("7 نتيجة")).toBeVisible();
    fireEvent.click(screen.getByRole("button", { name: "إعادة الضبط" }));
    expect(reset).toHaveBeenCalledOnce();
  });
});
