import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { DevelopmentEntry } from "./development-entry";

afterEach(()=>{cleanup();vi.restoreAllMocks();});

it("opens the selected authorized sport enrollment rather than a player identifier",async()=>{
  global.fetch=vi.fn(async()=>Response.json({enrollments:[{id:"enrollment-demo",player:"لاعب اصطناعي",playerCode:"DEMO-01",sport:"كرة القدم",group:"مجموعة العرض"}]}));
  render(<DevelopmentEntry title="أهداف تطور اللاعبين" destination="/dashboard/evaluations/goals"/>);
  expect(await screen.findByRole("cell",{name:"كرة القدم"})).toBeVisible();
  expect(screen.getByRole("link",{name:"فتح"})).toHaveAttribute("href","/dashboard/evaluations/goals?enrollmentId=enrollment-demo");
  fireEvent.change(screen.getByRole("textbox",{name:"ابحث عن اللاعب"}),{target:{value:"غير موجود"}});
  expect(screen.getByText("لا يوجد لاعب مطابق")).toBeVisible();
  expect(vi.mocked(fetch).mock.calls).toHaveLength(1);
});

it("offers no record links when the server rejects the enrollment list",async()=>{
  global.fetch=vi.fn(async()=>Response.json({message:"internal diagnostic"},{status:403}));
  render(<DevelopmentEntry title="رحلة المهارات" destination="/dashboard/evaluations/skills/journey"/>);
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر تحميل اللاعبين المتاحين ضمن صلاحياتك");
  expect(screen.queryByRole("link",{name:"فتح"})).not.toBeInTheDocument();
  expect(screen.queryByText("internal diagnostic")).not.toBeInTheDocument();
});
