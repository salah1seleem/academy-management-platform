import {cleanup,fireEvent,render,screen,within} from "@testing-library/react";
import {afterEach,expect,it,vi} from "vitest";
import {MatchesWorkspace} from "./matches";
vi.mock("next/navigation",()=>({useSearchParams:()=>new URLSearchParams()}));
afterEach(()=>{cleanup();vi.restoreAllMocks();});
it("opens exactly three list-first tabs, creation is separate and returns to list",async()=>{
 global.fetch=vi.fn(async path=>Response.json(String(path).includes("structure")?{sports:[],branches:[],categories:[],groups:[]}:String(path).includes("options")?{tournaments:[],teams:[],players:[],coaches:[]} : [])) as typeof fetch;
 render(<MatchesWorkspace/>);await screen.findByRole("button",{name:"إنشاء بطولة"});
 const tabs=screen.getByRole("navigation",{name:"أقسام البطولات والمباريات"});expect(within(tabs).getAllByRole("button")).toHaveLength(3);
 expect(screen.queryByRole("textbox",{name:"اسم البطولة"})).not.toBeInTheDocument();
 fireEvent.click(screen.getByRole("button",{name:"إنشاء بطولة"}));expect(screen.getByRole("textbox",{name:"اسم البطولة"})).toBeVisible();
 fireEvent.click(screen.getByRole("button",{name:"← العودة إلى القائمة"}));expect(screen.queryByRole("textbox",{name:"اسم البطولة"})).not.toBeInTheDocument();
 fireEvent.click(within(tabs).getByRole("button",{name:"الفرق والمنتخبات"}));expect(screen.getByRole("table",{name:"الفرق والمنتخبات"})).toBeVisible();expect(screen.queryByRole("textbox",{name:"اسم الفريق / المنتخب"})).not.toBeInTheDocument();
});
