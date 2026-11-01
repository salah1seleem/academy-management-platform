import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { StructureList } from "./structure-list";

vi.mock("next/navigation", () => ({ useSearchParams: () => new URLSearchParams(), usePathname: () => "/dashboard/academy/branches", useRouter: () => ({ replace: vi.fn() }) }));
const branches = [{ id: "1", arabicName: "فرع مدينة نصر", isActive: true }, { id: "2", arabicName: "فرع التجمع الخامس", isActive: true }];

beforeEach(() => { global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => branches }); });
afterEach(cleanup);

describe("structure list", () => {
  it("separates group data into labelled columns and keeps coach search and inactive filtering usable",async()=>{
    global.fetch=vi.fn().mockResolvedValue({ok:true,json:async()=>[
      {id:"demo-group-a",arabicName:"مجموعة النور التجريبية",isActive:true,branchName:"فرع الشروق",sportName:"كرة القدم",coaches:["مدرب تجريبي أول"],playerCount:8,schedules:[]},
      {id:"demo-group-b",arabicName:"مجموعة القمر التجريبية",isActive:false,branchName:"فرع مدينتي",sportName:"سباحة",coaches:["مدرب تجريبي ثان"],playerCount:3,schedules:[]},
    ]});
    render(<StructureList kind="groups"/>);
    const table=await screen.findByRole("table",{name:"قائمة المجموعات"});
    expect(within(table).getByRole("columnheader",{name:"الفرع"})).toBeVisible();
    expect(within(table).getByRole("columnheader",{name:"اللاعبون"})).toBeVisible();
    fireEvent.change(screen.getByRole("textbox",{name:"بحث المجموعات"}),{target:{value:"مدرب تجريبي ثان"}});
    expect(screen.queryByText("مجموعة النور التجريبية")).not.toBeInTheDocument();
    expect(screen.getByText("مجموعة القمر التجريبية")).toBeVisible();
    fireEvent.change(screen.getByRole("combobox",{name:"الحالة"}),{target:{value:"true"}});
    expect(screen.queryByRole("table",{name:"قائمة المجموعات"})).not.toBeInTheDocument();
    expect(screen.getByText("لا توجد سجلات مطابقة.")).toBeVisible();
    fireEvent.click(screen.getByRole("button",{name:"إعادة الضبط"}));
    expect(screen.getByText("مجموعة النور التجريبية")).toBeVisible();
  });
  it("filters the rendered tenant-scoped items and exposes only functional actions", async () => {
    render(<StructureList kind="branches" />);
    expect(await screen.findByText("فرع مدينة نصر")).toBeVisible();
    fireEvent.change(screen.getByRole("textbox", { name: "بحث الفروع" }), { target: { value: "التجمع" } });
    await waitFor(() => expect(screen.queryByText("فرع مدينة نصر")).not.toBeInTheDocument());
    expect(screen.getByText("فرع التجمع الخامس")).toBeVisible();
    expect(screen.getByRole("link", { name: "عرض" })).toHaveAttribute("href", "/dashboard/academy/branches/2");
    fireEvent.click(screen.getByLabelText("المزيد من الإجراءات"));
    expect(screen.getByRole("menuitem", { name: "تعديل" })).toHaveAttribute("href", "/dashboard/academy/branches/2/edit");
    fireEvent.click(screen.getByRole("menuitem", { name: "إيقاف" }));
    expect(screen.getByRole("dialog")).toHaveTextContent("فرع التجمع الخامس");
  });
  it("offers status control and keeps a selected coach in the assignment link", async () => {
    global.fetch = vi.fn().mockResolvedValue({ ok: true, json: async () => [{ id: "coach-1", arabicName: "كريم حسن", isActive: true, assignedGroups: 1 }] });
    render(<StructureList kind="coaches" />);
    expect(await screen.findByText("كريم حسن")).toBeVisible();
    fireEvent.click(screen.getByLabelText("المزيد من الإجراءات"));
    expect(screen.getByRole("menuitem", { name: "إسناد مجموعة" })).toHaveAttribute("href", "/dashboard/academy/coaches/new?coachId=coach-1");
    fireEvent.click(screen.getByRole("menuitem", { name: "إيقاف" }));
    expect(screen.getByRole("dialog")).toHaveTextContent("كريم حسن");
  });
});
