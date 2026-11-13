import {afterEach,expect,it,vi} from "vitest";
import {cleanup,fireEvent,render,screen,waitFor} from "@testing-library/react";
import {PlatformAcademyEditor} from "./platform-academy-editor";
import {platformRequest,PlatformTimeZoneLockedError} from "../../features/platform-api";
vi.mock("../../features/platform-api",async importOriginal=>({...await importOriginal<typeof import("../../features/platform-api")>(),platformRequest:vi.fn(async()=>new Response('{}'))}));
vi.mock("../../features/academy-import",()=>({AcademyImport:()=>null}));
vi.mock("./platform-launcher-icons",()=>({PlatformLauncherIcons:()=>null}));
const detail={id:"a",arabicName:"أكاديمية مصر",slug:"academy",timeZone:"Africa/Cairo",defaultCurrency:"EGP",isActive:false,profile:{editVersion:2,operationalState:"Suspended",location:"",contactPhone:"",commercialState:"Unspecified",platformFee:null,feeCurrency:"EGP",billingPeriod:"Monthly",subscriptionFrom:null,subscriptionTo:null},branding:null,branches:[],sports:[],users:[],readiness:["ارفع شعار الأكاديمية"],audit:[]};
afterEach(()=>{cleanup();vi.restoreAllMocks();vi.clearAllMocks();});
function open(read=()=>detail){global.fetch=vi.fn(async url=>Response.json(String(url).endsWith("/entitlements")?[]:read()));render(<PlatformAcademyEditor academyId="a" onBack={()=>{}} onChanged={()=>{}}/>);}
it("reactivation is clickable despite legacy readiness and requests the current version",async()=>{open();const button=await screen.findByRole("button",{name:"تفعيل الأكاديمية"});expect(button).toBeEnabled();vi.spyOn(window,"confirm").mockReturnValue(true);fireEvent.click(button);await waitFor(()=>expect(platformRequest).toHaveBeenCalledWith("/api/v1/platform/academies/a/state","PUT",{target:"Active",version:2}));expect(screen.queryByRole("button",{name:"تحديث الجاهزية"})).not.toBeInTheDocument();});
it("subscription writes only commercial fields even when setup is incomplete",async()=>{open();await screen.findByRole("button",{name:"تفعيل الأكاديمية"});fireEvent.click(screen.getByRole("tab",{name:"اشتراك المنصة"}));fireEvent.change(screen.getByLabelText("رسوم المنصة"),{target:{value:"1200"}});fireEvent.submit(screen.getByRole("button",{name:"حفظ اشتراك المنصة"}).closest("form")!);await waitFor(()=>expect(platformRequest).toHaveBeenCalledWith("/api/v1/platform/academies/a/subscription","PUT",expect.objectContaining({version:2,platformFee:1200})));expect(vi.mocked(platformRequest).mock.calls[0][2]).not.toHaveProperty("ownerPhone");expect(vi.mocked(platformRequest).mock.calls[0][2]).not.toHaveProperty("location");});

const reason="المنطقة الزمنية ثابتة بعد ترحيل تحصيلات سابقة.";

async function identity() {
  fireEvent.click(await screen.findByRole("tab",{name:"الهوية"}));
  return screen.getByRole("button",{name:"حفظ بيانات التجهيز"}).closest("form")!;
}

it("keeps a locked time zone readable and saves the other setup fields",async()=>{
  open(()=>({...detail,timeZoneLockReason:reason}));
  const form=await identity();
  const field=screen.getByLabelText("المنطقة الزمنية");
  expect(field).toHaveAttribute("readonly");
  expect(field).toHaveAccessibleDescription(reason);
  expect(field).toHaveValue("Africa/Cairo");
  fireEvent.change(screen.getByLabelText("الموقع"),{target:{value:"موقع الاختبار"}});
  fireEvent.submit(form);
  await waitFor(()=>expect(platformRequest).toHaveBeenCalledWith("/api/v1/platform/academies/a/setup","PUT",expect.objectContaining({location:"موقع الاختبار",timeZone:"Africa/Cairo"})));
});

it("recovers a stale editable zone without clearing the other draft fields",async()=>{
  open();
  const form=await identity();
  const timeZone=()=>screen.getByLabelText("المنطقة الزمنية");
  expect(timeZone()).not.toHaveAttribute("readonly");
  fireEvent.change(timeZone(),{target:{value:"Pacific/Honolulu"}});
  fireEvent.change(screen.getByLabelText("الموقع"),{target:{value:"تعديل موقع قيد الحفظ"}});
  vi.mocked(platformRequest).mockRejectedValueOnce(new PlatformTimeZoneLockedError(reason,"Africa/Cairo"));
  fireEvent.submit(form);
  expect(await screen.findByRole("alert")).toHaveTextContent("احفظ بقية التعديلات");
  expect(timeZone()).toHaveValue("Africa/Cairo");
  expect(timeZone()).toHaveAttribute("readonly");
  expect(timeZone()).toHaveAccessibleDescription(reason);
  expect(screen.getByLabelText("الموقع")).toHaveValue("تعديل موقع قيد الحفظ");
  fireEvent.submit(form);
  await waitFor(()=>expect(platformRequest).toHaveBeenLastCalledWith("/api/v1/platform/academies/a/setup","PUT",expect.objectContaining({location:"تعديل موقع قيد الحفظ",timeZone:"Africa/Cairo"})));
});

it("reads the saved zone and profile version back after an allowed change",async()=>{
  let current=detail;
  open(()=>current);
  const form=await identity();
  fireEvent.change(screen.getByLabelText("المنطقة الزمنية"),{target:{value:" Pacific/Honolulu "}});
  vi.mocked(platformRequest).mockImplementationOnce(async()=>{
    current={...detail,timeZone:"Pacific/Honolulu",profile:{...detail.profile,editVersion:3}};
    return Response.json({version:3});
  });
  fireEvent.submit(form);
  await waitFor(()=>expect(screen.getByLabelText("المنطقة الزمنية")).toHaveValue("Pacific/Honolulu"));
  expect(screen.getByLabelText("المنطقة الزمنية")).not.toHaveAttribute("readonly");
});
