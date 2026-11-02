import {cleanup,fireEvent,render,screen,waitFor} from "@testing-library/react";
import {afterEach,expect,it,vi} from "vitest";
import {RecordFollowUpCreateForm} from "./record-followup-create";
const push=vi.fn();vi.mock("next/navigation",()=>({useRouter:()=>({push})}));
afterEach(()=>{cleanup();vi.restoreAllMocks();push.mockReset();});
it("creates a related record task from a verified source scope without a fake group and returns to its detail",async()=>{
 const source={id:"employee",name:"منسق اصطناعي",branchId:"branch",branch:"فرع اصطناعي",sportId:null,sport:null};
 const fetcher=vi.fn(async(url,init)=>Response.json(String(url).endsWith("/csrf")?{token:"csrf"}:init?.method==="POST"?{id:"task"}:{items:[source],owners:[{id:"owner",name:"مالك اصطناعي"}],notice:"ليس صرفًا أو خصمًا"}));global.fetch=fetcher as typeof fetch;
 render(<RecordFollowUpCreateForm/>);fireEvent.change(screen.getByLabelText("نوع السجل"),{target:{value:"Employee"}});await screen.findByRole("option",{name:/منسق اصطناعي/});fireEvent.change(screen.getByLabelText("السجل المرتبط"),{target:{value:"employee/branch/"}});fireEvent.change(screen.getByLabelText("الأولوية"),{target:{value:"2"}});fireEvent.click(screen.getByRole("button",{name:"حفظ المتابعة"}));await waitFor(()=>expect(push).toHaveBeenCalledWith("/dashboard/operations/follow-ups/task"));
 const body=JSON.parse(fetcher.mock.calls.find(([,init])=>init?.method==="POST")![1]!.body as string);expect(body).toMatchObject({kind:"Employee",sourceId:"employee",branchId:"branch",sportId:null,priority:2});expect(body.trainingGroupId).toBeUndefined();expect(body.requestId).toEqual(expect.any(String));
});
