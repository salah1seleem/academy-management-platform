import {expect,it} from "vitest";
import {operationReturnPath,operationLoginPath} from "./operation-return";
it("resumes only the existing operation forms and never external or encoded destinations",()=>{
  for(const path of ["/dashboard/players/new","/dashboard/subscriptions/renew/new","/dashboard/academy/coaches/create","/dashboard/evaluations/new"]){
    expect(operationReturnPath(path)).toBe(path);expect(operationLoginPath(path)).toBe(`/login?returnTo=${encodeURIComponent(path)}`);
  }
  for(const path of [null,"https://evil.test","//evil.test","/dashboard/players/new?token=secret","%2Fdashboard%2Fplayers%2Fnew","/dashboard/../platform","/dashboard/staff","javascript:alert(1)"]){
    expect(operationReturnPath(path)).toBeNull();expect(operationLoginPath(path??"")).toBe("/login");
  }
});
