import {act,cleanup,fireEvent,render,screen} from "@testing-library/react";
import {afterEach,expect,it,vi} from "vitest";
import {DateFilterDefaults} from "../components/date-filter-defaults";
import {AttendanceWorkspace} from "./attendance-workspace";

const query=new URLSearchParams();
vi.mock("next/navigation",()=>({useSearchParams:()=>query}));
vi.mock("./attendance",()=>({AttendanceHistory:()=> <label>من تاريخ<input type="date" defaultValue="2026-11-01"/><span>سجل اللاعبين المحفوظ</span></label>}));
vi.mock("./attendance-corrections",()=>({AttendanceCorrections:()=> <p>طلبات التصحيح</p>}));
afterEach(()=>{cleanup();vi.restoreAllMocks();});

function delayedPermissions(){
  let release!:(value:Response)=>void;
  const pending=new Promise<Response>(resolve=>{release=resolve;});
  global.fetch=vi.fn(input=>String(input).endsWith("my-permissions")?pending:Promise.resolve(new Response(JSON.stringify(String(input).includes("structure/options")?{sports:[],branches:[],groups:[]}:{totalCount:0,items:[]})))) as typeof fetch;
  return async(grants:string[])=>act(async()=>release(new Response(JSON.stringify(grants))));
}

it("waits for report permission without mounting editable history that discards the user's dates",async()=>{
  const release=delayedPermissions();
  render(<DateFilterDefaults range={{from:"2026-11-01",to:"2026-11-02"}}><AttendanceWorkspace initialTab="reports"/></DateFilterDefaults>);
  expect(screen.getByRole("heading",{name:"تقارير الحضور"})).toBeVisible();
  expect(screen.getByRole("status")).toBeVisible();
  expect(screen.queryByLabelText("من تاريخ")).not.toBeInTheDocument();
  expect(screen.queryByText("سجل اللاعبين المحفوظ")).not.toBeInTheDocument();
  await release(["reports.attendance.read"]);
  fireEvent.change(await screen.findByLabelText("من تاريخ"),{target:{value:"2026-09-01"}});
  fireEvent.change(screen.getByLabelText("إلى تاريخ"),{target:{value:"2026-09-27"}});
  fireEvent.click(screen.getByRole("button",{name:"تطبيق الفلاتر"}));
  expect(global.fetch).toHaveBeenCalledWith(expect.stringContaining("from=2026-09-01&to=2026-09-27"));
  expect(screen.getByLabelText("من تاريخ")).toHaveValue("2026-09-01");
  expect(screen.getByRole("link",{name:"تصدير CSV"})).toHaveAttribute("href",expect.stringContaining("from=2026-09-01&to=2026-09-27"));
});

it("keeps the history fallback when the loaded membership lacks report permission",async()=>{
  const release=delayedPermissions();
  render(<AttendanceWorkspace initialTab="reports"/>);
  await release([]);
  expect(await screen.findByText("سجل اللاعبين المحفوظ")).toBeVisible();
  expect(screen.queryByRole("button",{name:"التحليل والتصدير"})).not.toBeInTheDocument();
  expect(vi.mocked(fetch).mock.calls.some(([url])=>String(url).includes("reports/attendance"))).toBe(false);
});

it("does not block the explicitly requested history while permissions are pending",()=>{
  delayedPermissions();
  render(<AttendanceWorkspace/>);
  expect(screen.getByText("سجل اللاعبين المحفوظ")).toBeVisible();
});

it("leaves the loading state without exposing reports when the permission request fails",async()=>{
  global.fetch=vi.fn(()=>Promise.reject(new Error("synthetic network failure"))) as typeof fetch;
  render(<AttendanceWorkspace initialTab="reports"/>);
  expect(await screen.findByText("سجل اللاعبين المحفوظ")).toBeVisible();
  expect(screen.queryByRole("status")).not.toBeInTheDocument();
  expect(screen.queryByRole("button",{name:"التحليل والتصدير"})).not.toBeInTheDocument();
});
