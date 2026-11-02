import { cleanup,render,screen } from "@testing-library/react";
import { afterEach,expect,it,vi } from "vitest";
import { useStaffAction } from "./use-staff-action";
afterEach(()=>{cleanup();vi.restoreAllMocks();});
function Check({module,action}:{module:string;action:"read"|"edit"|"add"|"delete"}){return <span>{useStaffAction(module,action)?"allowed":"denied"}</span>;}
function mock(permissions:string[],actions?:object[]){global.fetch=vi.fn(async url=>new Response(JSON.stringify(String(url).endsWith("/me")?{role:"AcademyAdmin"}:String(url).endsWith("/my-access")?{useActionGrants:!!actions,actions}:permissions),{status:200})) as typeof fetch;}
it("preserves legacy administrator structure edit without inventing delete authority",async()=>{mock(["structure.manage"]);const {rerender}=render(<Check module="academy" action="edit"/>);await screen.findByText("allowed");rerender(<Check module="academy" action="delete"/>);await screen.findByText("denied");});
it("legacy facility read does not imply admission or checkout authority",async()=>{mock(["facilities.read"]);const {rerender}=render(<Check module="facilities" action="read"/>);await screen.findByText("allowed");rerender(<Check module="facilities" action="add"/>);await screen.findByText("denied");});
it("explicit CRUD grants override a legacy broad permission",async()=>{mock(["structure.manage"],[{module:"academy",read:true,edit:false}]);render(<Check module="academy" action="edit"/>);expect(await screen.findByText("denied")).toBeVisible();});
