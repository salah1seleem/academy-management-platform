import { cleanup,fireEvent,render,screen } from "@testing-library/react";
import { afterEach,expect,it,vi } from "vitest";
import { StaffVerifiedAttendance } from "./staff-verified-attendance";
vi.mock("./staff-passkeys",()=>({browserPasskey:vi.fn(async()=>({rawId:"proof",response:{signature:"signed"}}))}));
const state={configured:true,hasCredential:true,hasOpenAttendance:false,requiresSourceReview:false,branches:[{id:"site",name:"فرع عرض اصطناعي",configured:true,enabled:true,radiusMeters:80,maxAccuracyMeters:10,maxAgeSeconds:15,challengeSeconds:60,attendanceRequired:true}],recent:[]};
const response=(x:unknown,status=200)=>Promise.resolve(new Response(JSON.stringify(x),{status}));
afterEach(()=>{cleanup();vi.restoreAllMocks();vi.unstubAllGlobals();sessionStorage.clear();});
function location(fail=false){vi.stubGlobal("isSecureContext",true);vi.stubGlobal("navigator",{geolocation:{getCurrentPosition:vi.fn((ok:(v:unknown)=>void,bad:()=>void)=>fail?bad():ok({timestamp:Date.now(),coords:{latitude:30.05,longitude:31.3,accuracy:5}}))}});}
it("renders the unconfigured response without optional branch arrays and never offers a confirmation",async()=>{
  global.fetch=vi.fn(()=>response({configured:false})) as typeof fetch;
  render(<StaffVerifiedAttendance/>);
  expect(await screen.findByText("يلزم إعداد التحقق الآمن من المسؤول؛ مسار المراجعة الإداري متاح.")).toBeVisible();
  expect(screen.getByRole("heading",{name:"حضوري وانصرافي"})).toBeVisible();
  expect(screen.queryByRole("button",{name:"تسجيل حضوري"})).not.toBeInTheDocument();
  expect(vi.mocked(fetch).mock.calls.every(([,options])=>!options?.method)).toBe(true);
});
it.each([
  [{ configured: false, profileMissing: true }, "لا يوجد ملف موظف مرتبط بحسابك. تواصل مع الإدارة."],
  [{ ...state, hasCredential: false }, /أضف وسيلة تحقق فعالة/],
])("explains an unavailable clock without offering a write: %j", async (initial, message) => {
  global.fetch = vi.fn(() => response(initial));
  render(<StaffVerifiedAttendance />);
  await screen.findByText(message);
  expect(screen.queryByRole("button", { name: "تسجيل حضوري" })).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "تسجيل انصرافي" })).not.toBeInTheDocument();
});
it("confirms server attendance only after location, operation-bound options and signed complete; no client biometric boolean",async()=>{
  location();let done=false;global.fetch=vi.fn(p=>String(p).includes("csrf")?response({token:"t"}):String(p).endsWith("/options")?response({purpose:"CheckIn",challengeId:"one",options:{challenge:"AQ"}}):String(p).endsWith("/complete")?(done=true,response({attendanceRecorded:true,attendanceId:"day",workPeriodId:"period",operation:"CheckIn",confirmedAtUtc:"2026-11-09T10:00:00Z",trustSource:"ServerVerified"})):response({...state,hasOpenAttendance:done})) as typeof fetch;
  render(<StaffVerifiedAttendance/>);await screen.findByRole("option",{name:"فرع عرض اصطناعي"});fireEvent.change(screen.getByLabelText("فرع الحضور"),{target:{value:"site"}});fireEvent.click(screen.getByRole("button",{name:"تسجيل حضوري"}));expect(await screen.findByText("تم تسجيل حضورك بوقت الخادم المؤكد.")).toBeVisible();
  const call=vi.mocked(fetch).mock.calls.find(([p])=>String(p).endsWith("/complete"))!;expect(JSON.parse(String(call[1]?.body))).toMatchObject({challengeId:"one",intent:{branchId:"site",operation:"CheckIn",accuracyMeters:5},credential:{rawId:"proof"}});expect(String(call[1]?.body)).not.toContain("biometric");expect(screen.getByRole("button",{name:"تسجيل انصرافي"})).toBeEnabled();
});
it("location denial never submits options or claims success",async()=>{
  location(true);global.fetch=vi.fn(()=>response(state)) as typeof fetch;render(<StaffVerifiedAttendance/>);await screen.findByRole("option",{name:"فرع عرض اصطناعي"});fireEvent.change(screen.getByLabelText("فرع الحضور"),{target:{value:"site"}});fireEvent.click(screen.getByRole("button",{name:"تسجيل حضوري"}));expect(await screen.findByText(/تعذر التحقق من الموقع أو التوقيع/)).toBeVisible();expect(vi.mocked(fetch).mock.calls.every(([,o])=>!o?.method)).toBe(true);
});
it("a lost reply retains the exact signed command and retries it, not a new nonce or location",async()=>{
  location();let failed=true;global.fetch=vi.fn(p=>String(p).includes("csrf")?response({token:"t"}):String(p).endsWith("/options")?response({purpose:"CheckIn",challengeId:"same",options:{}}):String(p).endsWith("/attempts/same")?response({},404):String(p).endsWith("/complete")?(failed?(failed=false,Promise.reject(new Error("network"))):response({attendanceRecorded:true,attendanceId:"day",workPeriodId:"period",operation:"CheckIn",confirmedAtUtc:"2026-11-09T10:00:00Z",trustSource:"ServerVerified"})):response(state)) as typeof fetch;
  render(<StaffVerifiedAttendance/>);await screen.findByRole("option",{name:"فرع عرض اصطناعي"});fireEvent.change(screen.getByLabelText("فرع الحضور"),{target:{value:"site"}});fireEvent.click(screen.getByRole("button",{name:"تسجيل حضوري"}));
  await screen.findByRole("button",{name:"إعادة إرسال الطلب نفسه"});
  fireEvent.click(screen.getByRole("button",{name:"التحقق من نتيجة التسجيل"}));
  expect(await screen.findByText(/لم تُحفظ نتيجة مؤكدة لهذه المحاولة/)).toBeVisible();
  expect(screen.queryByRole("button",{name:"تسجيل حضوري"})).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole("button",{name:"إعادة إرسال الطلب نفسه"}));expect(await screen.findByText("تم تسجيل حضورك بوقت الخادم المؤكد.")).toBeVisible();const calls=vi.mocked(fetch).mock.calls.filter(([p])=>String(p).endsWith("/complete"));expect(calls).toHaveLength(2);expect(calls[0][1]?.body).toBe(calls[1][1]?.body);
});
it("displays the selected live branch policy rather than fixed 50m and distinguishes disabled branches",async()=>{
  global.fetch=vi.fn(()=>response({...state,branches:[...state.branches,{...state.branches[0],id:"off",name:"فرع موقوف",enabled:false}]})) as typeof fetch;render(<StaffVerifiedAttendance/>);await screen.findByLabelText("فرع الحضور");fireEvent.change(screen.getByLabelText("فرع الحضور"),{target:{value:"site"}});expect(screen.getByLabelText("سياسة الفرع المختار")).toHaveTextContent("النطاق 80 مترًا · الدقة حتى 10 مترًا · قراءة خلال 15 ثانية · محاولة التوقيع 60 ثانية");expect(screen.getByLabelText("سياسة الفرع المختار")).toHaveTextContent("تسجيل الحضور مطلوب لفئتك");expect(screen.getByRole("option",{name:"فرع موقوف — التسجيل الذاتي موقوف"})).toBeDisabled();
});
it("recovers the exact attendance receipt by read after the signed response is lost",async()=>{
  location();let posted=false;
  const receipt={attendanceRecorded:true,attendanceId:"day",workPeriodId:"period",operation:"CheckIn",confirmedAtUtc:"2026-11-09T10:00:00Z",trustSource:"ServerVerified"};
  global.fetch=vi.fn(p=>String(p).includes("csrf")?response({token:"t"}):String(p).endsWith("/options")?response({purpose:"CheckIn",challengeId:"lost",options:{}}):String(p).endsWith("/complete")?(posted=true,Promise.reject(new TypeError("network"))):String(p).endsWith("/attempts/lost")?response(receipt):response({...state,hasOpenAttendance:posted})) as typeof fetch;
  render(<StaffVerifiedAttendance/>);await screen.findByRole("option",{name:"فرع عرض اصطناعي"});fireEvent.change(screen.getByLabelText("فرع الحضور"),{target:{value:"site"}});fireEvent.click(screen.getByRole("button",{name:"تسجيل حضوري"}));
  fireEvent.click(await screen.findByRole("button",{name:"التحقق من نتيجة التسجيل"}));
  expect(await screen.findByText("تم تسجيل حضورك بوقت الخادم المؤكد.")).toBeVisible();
  expect(vi.mocked(fetch).mock.calls.filter(([p])=>String(p).endsWith("/complete"))).toHaveLength(1);
  expect(vi.mocked(fetch).mock.calls.filter(([p])=>String(p).endsWith("/attempts/lost"))).toHaveLength(1);
});
it("unlocks an expired attempt after re-reading the current branch state",async()=>{
  location();let writes=0;
  global.fetch=vi.fn(p=>String(p).includes("csrf")?response({token:"t"}):String(p).endsWith("/options")?response({purpose:"CheckIn",challengeId:"expired",options:{}}):String(p).endsWith("/complete")?(++writes===1?Promise.reject(new TypeError("offline")):response({message:"انتهت محاولة التسجيل."},409)):response(state)) as typeof fetch;
  render(<StaffVerifiedAttendance/>);await screen.findByRole("option",{name:"فرع عرض اصطناعي"});fireEvent.change(screen.getByLabelText("فرع الحضور"),{target:{value:"site"}});fireEvent.click(screen.getByRole("button",{name:"تسجيل حضوري"}));
  fireEvent.click(await screen.findByRole("button",{name:"إعادة إرسال الطلب نفسه"}));
  expect(await screen.findByText("انتهت محاولة التسجيل.")).toBeVisible();
  expect(await screen.findByRole("button",{name:"تسجيل حضوري"})).toBeEnabled();
  expect(screen.queryByRole("button",{name:"إعادة إرسال الطلب نفسه"})).not.toBeInTheDocument();
  const bodies=vi.mocked(fetch).mock.calls.filter(([p])=>String(p).endsWith("/complete")).map(([,options])=>options?.body);
  expect(bodies).toEqual([bodies[0],bodies[0]]);
});
it("keeps a confirmed checkout visible when the branch state cannot refresh",async()=>{
  location();let reads=0;
  const receipt={attendanceRecorded:true,attendanceId:"day",workPeriodId:"period",operation:"CheckOut",confirmedAtUtc:"2026-11-09T10:00:00Z",trustSource:"ServerVerified"};
  global.fetch=vi.fn(p=>String(p).includes("csrf")?response({token:"t"}):String(p).endsWith("/options")?response({purpose:"CheckOut",challengeId:"end",options:{}}):String(p).endsWith("/complete")?Promise.reject(new TypeError("offline")):String(p).endsWith("/attempts/end")?response(receipt):++reads===1?response({...state,hasOpenAttendance:true}):response({},503)) as typeof fetch;
  render(<StaffVerifiedAttendance/>);await screen.findByRole("option",{name:"فرع عرض اصطناعي"});fireEvent.change(screen.getByLabelText("فرع الحضور"),{target:{value:"site"}});fireEvent.click(screen.getByRole("button",{name:"تسجيل انصرافي"}));
  fireEvent.click(await screen.findByRole("button",{name:"التحقق من نتيجة التسجيل"}));
  expect(await screen.findByText(/تم تسجيل انصرافك للفترة نفسها/)).toBeVisible();
  expect(await screen.findByText(/العملية مؤكدة. تعذر تحديث الحالة/)).toBeVisible();
  expect(screen.queryByRole("button",{name:"تسجيل حضوري"})).not.toBeInTheDocument();
  expect(screen.queryByRole("button",{name:"إعادة إرسال الطلب نفسه"})).not.toBeInTheDocument();
});

const membershipId = "40000000-0000-0000-0000-000000000001";
const challengeId = "40000000-0000-0000-0000-000000000002";
const key = `academy-staff-clock:${membershipId}`;
const recoveredPath = `/attempts/${challengeId}?includeState=true`;
const identified = { ...state, membershipId };
const savedAttendance = {
  attendanceRecorded: true, attendanceId: "day", workPeriodId: "period",
  operation: "CheckIn", confirmedAtUtc: "2026-11-12T05:00:00Z", trustSource: "ServerVerified",
};
function seedAttempt(scope = key) {
  sessionStorage.setItem(scope, JSON.stringify({ challengeId, operation: "CheckIn" }));
}

it("restores the exact confirmed attempt after refresh, then reads the current branch state", async () => {
  seedAttempt();
  global.fetch = vi.fn(path => String(path).endsWith(recoveredPath)
    ? response(savedAttendance) : response({ ...identified, hasOpenAttendance: true }));
  render(<StaffVerifiedAttendance />);
  await screen.findByText("تم تسجيل حضورك بوقت الخادم المؤكد.");
  expect(await screen.findByRole("button", { name: "تسجيل انصرافي" })).toBeDisabled();
  expect(sessionStorage.getItem(key)).toBeNull();
  expect(vi.mocked(fetch).mock.calls.filter(([path]) => String(path).endsWith(recoveredPath))).toHaveLength(1);
  expect(vi.mocked(fetch).mock.calls.every(([, init]) => !init?.method)).toBe(true);
});

it("keeps a restored attempt read-only until the server confirms it closed without recording", async () => {
  seedAttempt();
  let isClosed = false;
  global.fetch = vi.fn(path => String(path).endsWith(recoveredPath)
    ? response({ attendanceRecorded: false, attemptState: isClosed ? "Closed" : "Pending" })
    : response(identified));
  render(<StaffVerifiedAttendance />);
  await screen.findByText(/لم يظهر تسجيل مؤكد بعد/);
  expect(screen.queryByLabelText("فرع الحضور")).not.toBeInTheDocument();
  expect(screen.queryByRole("button", { name: "إعادة إرسال الطلب نفسه" })).not.toBeInTheDocument();
  expect(sessionStorage.getItem(key)).toBeTruthy();
  isClosed = true;
  fireEvent.click(screen.getByRole("button", { name: "التحقق من نتيجة التسجيل" }));
  await screen.findByText(/انتهت المحاولة دون تسجيل/);
  expect(screen.getByLabelText("فرع الحضور")).toBeEnabled();
  expect(sessionStorage.getItem(key)).toBeNull();
  expect(vi.mocked(fetch).mock.calls.every(([, init]) => !init?.method)).toBe(true);
});

it.each([
  [{}, 503],
  [{ ...savedAttendance, operation: "CheckOut" }, 200],
  [{ ...savedAttendance, workPeriodId: null }, 200],
  [{ ...savedAttendance, confirmedAtUtc: "invalid" }, 200],
])("keeps the stored reference when recovery fails or the receipt differs (%j)", async (body, status) => {
  seedAttempt();
  let valid = false;
  global.fetch = vi.fn(path => String(path).endsWith(recoveredPath)
    ? valid ? response(savedAttendance) : response(body, status)
    : response(identified));
  render(<StaffVerifiedAttendance />);
  await screen.findByText(/تعذر التحقق من نتيجة التسجيل/);
  expect(screen.queryByRole("button", { name: "تسجيل حضوري" })).not.toBeInTheDocument();
  expect(sessionStorage.getItem(key)).toBeTruthy();
  valid = true;
  fireEvent.click(screen.getByRole("button", { name: "التحقق من نتيجة التسجيل" }));
  await screen.findByText("تم تسجيل حضورك بوقت الخادم المؤكد.");
  expect(sessionStorage.getItem(key)).toBeNull();
  expect(vi.mocked(fetch).mock.calls.every(([, init]) => !init?.method)).toBe(true);
});

it("never requests the prior membership's attempt in another account", async () => {
  seedAttempt();
  global.fetch = vi.fn(() => response({ ...state, membershipId: "different-membership" }));
  render(<StaffVerifiedAttendance />);
  await screen.findByLabelText("فرع الحضور");
  expect(sessionStorage.getItem(key)).toBeTruthy();
  expect(vi.mocked(fetch).mock.calls.some(([path]) => String(path).includes("/attempts/"))).toBe(false);
});

it.each([false, true])("keeps the exact proof in memory even when tab storage is unavailable: %s", async storageUnavailable => {
  if (storageUnavailable) vi.spyOn(Storage.prototype, "setItem").mockImplementation(() => { throw Error(); });
  location();
  let posted = false;
  global.fetch = vi.fn(path => {
    const url = String(path);
    if (url.endsWith("/csrf")) return response({ token: "csrf" });
    if (url.endsWith("/options")) return response({ challengeId, purpose: "CheckIn", options: {} });
    if (url.endsWith("/complete")) {
      if (posted) return response(savedAttendance);
      posted = true;
      return Promise.reject(new TypeError("Lost response"));
    }
    return response(identified);
  });
  render(<StaffVerifiedAttendance />);
  fireEvent.change(await screen.findByLabelText("فرع الحضور"), { target: { value: "site" } });
  fireEvent.click(screen.getByRole("button", { name: "تسجيل حضوري" }));
  const retry = await screen.findByRole("button", { name: "إعادة إرسال الطلب نفسه" });
  expect(JSON.parse(sessionStorage.getItem(key) ?? "null")).toEqual(
    storageUnavailable ? null : { challengeId, operation: "CheckIn" },
  );
  fireEvent.click(retry);
  await screen.findByText("تم تسجيل حضورك بوقت الخادم المؤكد.");
  const writes = vi.mocked(fetch).mock.calls.filter(([path]) => String(path).endsWith("/complete"));
  expect(writes).toHaveLength(2);
  expect(writes[0][1]?.body).toEqual(writes[1][1]?.body);
});

it("loads the current branch state before allowing a new attempt when the stored reference is gone", async () => {
  seedAttempt();
  let reads = 0;
  global.fetch = vi.fn(path => {
    if (String(path).endsWith(recoveredPath)) return response(null, 404);
    return ++reads === 2 ? response({}, 503) : response(identified);
  });
  render(<StaffVerifiedAttendance />);
  await screen.findByText(/تعذر التحقق من نتيجة التسجيل/);
  expect(screen.queryByLabelText("فرع الحضور")).not.toBeInTheDocument();
  expect(sessionStorage.getItem(key)).toBeTruthy();
  fireEvent.click(screen.getByRole("button", { name: "التحقق من نتيجة التسجيل" }));
  await screen.findByText(/لم تعد هذه المحاولة متاحة/);
  expect(screen.getByLabelText("فرع الحضور")).toBeEnabled();
  expect(sessionStorage.getItem(key)).toBeNull();
});
