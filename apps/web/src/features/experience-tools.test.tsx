import {cleanup,fireEvent,render,screen} from "@testing-library/react";
import {afterEach,expect,it,vi} from "vitest";
import {ExperienceTools} from "./experience-tools";
afterEach(()=>{cleanup();vi.restoreAllMocks();});
it("names icon-only controls independently of hidden mobile captions and keeps the unread badge separate",async()=>{
  global.fetch=vi.fn(async input=>new Response(JSON.stringify(String(input).includes("page-help")?{available:false}:{unread:2})));
  render(<ExperienceTools/>);
  expect(screen.getByRole("link",{name:/^التنبيهات$/})).toHaveAttribute("href","/dashboard/notifications");
  expect(await screen.findByLabelText("2 غير مقروءة")).toHaveTextContent("2");
  fireEvent.click(screen.getByRole("button",{name:/^شرح الصفحة$/}));
  expect(await screen.findByRole("dialog",{name:"شرح الصفحة"})).toHaveTextContent("لم يضف مالك الأكاديمية فيديو شرح لهذه الصفحة بعد.");
  fireEvent.click(screen.getByRole("button",{name:/^إغلاق$/}));
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  expect(screen.getByRole("button",{name:/^شرح الصفحة$/})).toHaveFocus();
  fireEvent.click(screen.getByRole("button",{name:/^شرح الصفحة$/}));
  await screen.findByRole("dialog",{name:"شرح الصفحة"});
  fireEvent.keyDown(document,{key:"Escape"});
  expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
});
