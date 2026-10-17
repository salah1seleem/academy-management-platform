import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { HelpAudienceEditor } from "./help-audience-editor";
import { ContextHelp } from "../components/context-help";
import { VersionedConfiguration } from "./versioned-configuration";
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const lessons = [{ id: "family-renewal", title: { ar: "تجديد اشتراك أبنائك", en: "Renew" }, roles: ["Guardian"], feature: "training" }];
it("offers only product-eligible roles and distinguishes empty audience from default", () => {
  const change = vi.fn(); const {rerender}=render(<HelpAudienceEditor lessons={lessons} disabled={false} onChange={change}/>);
  const field=screen.getByRole("checkbox",{name:"تجديد اشتراك أبنائك — ولي الأمر"});expect(field).toBeChecked();expect(screen.queryByLabelText(/المدرب/)).not.toBeInTheDocument();
  fireEvent.click(field);expect(change).toHaveBeenLastCalledWith({"family-renewal":[]});rerender(<HelpAudienceEditor lessons={lessons} value={{"family-renewal":[]}} disabled={false} onChange={change}/>);
  expect(screen.getByText("مخفي عن كل الأدوار في الأكاديمية")).toBeVisible();fireEvent.click(screen.getByRole("button",{name:"استعادة جمهور المنتج"}));expect(change).toHaveBeenLastCalledWith({});
});
it("owner saves audience into the existing draft, not directly into active settings",async()=>{
  const settings={currency:"EGP",timeZone:"Africa/Cairo",features:{learning_hub:true},cashEnabled:true,consecutiveAbsenceAlert:3,tenantMarketingOptIn:false,helpAudiences:null};
  global.fetch=vi.fn(async(input,init)=>Response.json(String(input).endsWith("my-permissions")?["academy.settings.manage"]:String(input).endsWith("/me")?{role:"AcademyOwner"}:String(input).endsWith("help-options")?{lessons}:String(input).endsWith("csrf")?{token:"csrf"}:init?{}:{entitlements:[],versions:[{id:"v1",revision:1,version:5,state:"Active",contentHash:"a".repeat(64),settings}],history:[]})) as typeof fetch;
  render(<VersionedConfiguration/>);fireEvent.click(await screen.findByRole("checkbox",{name:"تجديد اشتراك أبنائك — ولي الأمر"}));fireEvent.click(screen.getByRole("button",{name:"حفظ إصدار مسودة"}));
  await waitFor(()=>expect(vi.mocked(fetch).mock.calls.some(([p,i])=>String(p).endsWith("/drafts")&&JSON.parse(String(i?.body)).helpAudiences["family-renewal"].length===0)).toBe(true));
  expect(vi.mocked(fetch).mock.calls.some(([p])=>String(p).endsWith("transition"))).toBe(false);
});
it("settings administrator cannot see owner audience controls",async()=>{
  global.fetch=vi.fn(async input=>Response.json(String(input).endsWith("my-permissions")?["academy.settings.manage"]:String(input).endsWith("/me")?{role:"AcademyAdmin"}:String(input).endsWith("help-options")?{lessons}:{entitlements:[],versions:[],history:[]})) as typeof fetch;
  render(<VersionedConfiguration/>);await waitFor(()=>expect(fetch).toHaveBeenCalledWith("/api/v1/me",expect.anything()));expect(screen.queryByText("جمهور دليل الاستخدام")).not.toBeInTheDocument();
});
it("contextual link opens the exact authorized lesson and vanishes on revocation",async()=>{
  global.fetch=vi.fn().mockResolvedValueOnce(Response.json({id:"family-renewal"})).mockResolvedValue(Response.json({},{status:404}));render(<ContextHelp lesson="family-renewal" guardian/>);
  expect(await screen.findByRole("link",{name:"شرح هذه الخطوة"})).toHaveAttribute("href","/guardian/help/family-renewal");fireEvent(window,new Event("focus"));await waitFor(()=>expect(screen.queryByRole("link")).not.toBeInTheDocument());
});
it("contextual link is not manufactured after a failed authorization",async()=>{
  global.fetch=vi.fn(async()=>Response.json({},{status:404})) as typeof fetch;render(<ContextHelp lesson="player-attendance"/>);await waitFor(()=>expect(fetch).toHaveBeenCalledOnce());expect(screen.queryByRole("link")).not.toBeInTheDocument();
});
