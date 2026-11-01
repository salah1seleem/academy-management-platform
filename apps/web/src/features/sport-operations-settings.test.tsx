import { cleanup, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { SportOperationsSettings } from "./sport-operations-settings";
afterEach(() => { cleanup(); vi.restoreAllMocks(); vi.unstubAllGlobals(); });
const base = { facilitiesEnabled: true, sports: [{ id: "football", name: "كرة القدم", kind: "Football", isActive: true, version: 1 }, { id: "swim", name: "السباحة", kind: "Swimming", isActive: true, version: 2 }], branches: [{ id: "a", name: "فرع أ" }, { id: "b", name: "فرع ب" }] };
function setup(matchReadback = true) {
  let enabled = true, version = 10;
  const fetcher = vi.fn(async (url: string, init?: RequestInit) => {
    if (init?.method === "PUT" && matchReadback) {enabled = JSON.parse(String(init.body)).enabled; version++;}
    return {ok: true, status: 200, json: async () => url.endsWith("/csrf") ? {token: "token"} : url.endsWith("/swimming") ? {policies: [{branchId: "a", enabled, privateTrainingEnabled: false, openSwimEnabled: false, dayPassEnabled: true, version}], history: []} : init?.method === "PUT" ? {} : base};
  });
  vi.stubGlobal("fetch", fetcher); render(<SportOperationsSettings />); return fetcher;
}
it("does not show or request swimming settings in a football context", async () => {
  const fetcher = setup(); fireEvent.change(await screen.findByLabelText("الرياضة"), { target: { value: "football" } });
  expect(screen.queryByRole("region", { name: "إعدادات السباحة" })).not.toBeInTheDocument();
  expect(fetcher.mock.calls.some(([url]) => url.includes("/swimming"))).toBe(false);
  expect(screen.getByLabelText("نوع الرياضة")).toHaveValue("Football");
});
it("saves only the selected branch with its state version and preserves the other branch", async () => {
  const fetcher = setup(); fireEvent.change(await screen.findByLabelText("الرياضة"), { target: { value: "swim" } });
  const heading = await screen.findByRole("heading", { name: "فرع أ" }); const form = heading.closest("form")!;
  fireEvent.click(within(form).getByLabelText("تشغيل مرافق السباحة في هذا الفرع"));
  fireEvent.click(within(form).getByRole("button", { name: "حفظ إعدادات الفرع" }));
  await waitFor(() => expect(fetcher.mock.calls.some(([url, init]) => url.endsWith("/swimming/a") && init?.method === "PUT")).toBe(true));
  const sent = fetcher.mock.calls.find(([url, init]) => url.endsWith("/swimming/a") && init?.method === "PUT")!;
  expect(JSON.parse(sent[1]!.body as string)).toEqual({ enabled: false, privateTrainingEnabled: false, openSwimEnabled: false, dayPassEnabled: true, version: 10 });
  expect(fetcher.mock.calls.some(([url, init]) => url.endsWith("/swimming/b") && init?.method === "PUT")).toBe(false);
  expect(await screen.findByRole("status")).toHaveTextContent("تم حفظ إعدادات فرع أ");
});
it("shows the Arabic server failure without offering a working settings form", async () => {
  global.fetch = vi.fn().mockResolvedValue({ ok: false, status: 403 }); render(<SportOperationsSettings />);
  expect(await screen.findByText(/خصائص الرياضات متاحة للمالك فقط/)).toBeVisible();
  expect(screen.queryByLabelText("الرياضة")).not.toBeInTheDocument();
});
it("does not announce a branch save when readback still has different policy values", async () => {
  const notice=vi.fn();window.addEventListener('academy-save-success',notice);
  try {
    setup(false);fireEvent.change(await screen.findByLabelText('الرياضة'),{target:{value:'swim'}});
    const form=(await screen.findByRole('heading',{name:'فرع أ'})).closest('form')!;
    fireEvent.click(within(form).getByLabelText('تشغيل مرافق السباحة في هذا الفرع'));
    fireEvent.click(within(form).getByRole('button',{name:'حفظ إعدادات الفرع'}));
    await screen.findByText(/لم تتطابق إعدادات الفرع المقروءة/);
    expect(screen.queryByText('تم حفظ إعدادات فرع أ.')).not.toBeInTheDocument();
    expect(notice).not.toHaveBeenCalled();
    expect(within(form).getByLabelText('سباحة حرة')).toBeDisabled();
  } finally {window.removeEventListener('academy-save-success',notice);}
});
it("checks an uncertain branch save with GET only and keeps the submitted controls frozen", async () => {
  let enabled=true,writes=0;
  global.fetch=vi.fn(async (input,init)=>{
    const url=String(input);
    if(url.endsWith('/csrf'))return Response.json({token:'csrf-test'});
    if(init?.method==='PUT'){writes++;enabled=JSON.parse(String(init.body)).enabled;throw Error('انقطع الرد');}
    if(url.endsWith('/swimming'))return Response.json({policies:[{branchId:'a',enabled,privateTrainingEnabled:false,openSwimEnabled:false,dayPassEnabled:true,version:writes?11:10}],history:[]});
    return Response.json(base);
  });
  render(<SportOperationsSettings/>);
  fireEvent.change(await screen.findByLabelText('الرياضة'),{target:{value:'swim'}});
  const form=(await screen.findByRole('heading',{name:'فرع أ'})).closest('form')!;
  fireEvent.click(within(form).getByLabelText('تشغيل مرافق السباحة في هذا الفرع'));
  fireEvent.click(within(form).getByRole('button',{name:'حفظ إعدادات الفرع'}));
  const retry=await screen.findByRole('button',{name:'إعادة التحقق من الحفظ'});
  expect(within(form).getByLabelText('سباحة حرة')).toBeDisabled();
  expect(screen.getByLabelText('الرياضة')).toBeDisabled();
  fireEvent.click(retry);
  await screen.findByText('تم حفظ إعدادات فرع أ.');expect(writes).toBe(1);
  expect(screen.getByLabelText('الرياضة')).not.toBeDisabled();
});
it("rejects a mismatched sport kind without announcing success and reloads with GET only", async () => {
  const fetcher=setup();
  fireEvent.change(await screen.findByLabelText('الرياضة'),{target:{value:'football'}});
  fireEvent.change(screen.getByLabelText('نوع الرياضة'),{target:{value:'General'}});
  fireEvent.click(screen.getByRole('button',{name:'حفظ نوع الرياضة'}));
  await screen.findByText(/لم يتطابق نوع الرياضة المقروء/);
  expect(screen.queryByRole('status')).not.toBeInTheDocument();
  expect(screen.getByLabelText('نوع الرياضة')).toBeDisabled();
  fireEvent.click(screen.getByRole('button',{name:'إعادة تحميل الإعدادات الحالية'}));
  await waitFor(()=>expect(screen.getByLabelText('نوع الرياضة')).toHaveValue('Football'));
  expect(screen.getByLabelText('الرياضة')).not.toBeDisabled();
  expect(fetcher.mock.calls.filter(([,init])=>init?.method==='PUT')).toHaveLength(1);
});
it("preserves an unsaved branch B draft when branch A readback replaces its saved form", async () => {
  setup();fireEvent.change(await screen.findByLabelText('الرياضة'),{target:{value:'swim'}});
  const a=(await screen.findByRole('heading',{name:'فرع أ'})).closest('form')!;
  const b=screen.getByRole('heading',{name:'فرع ب'}).closest('form')!;
  fireEvent.click(within(b).getByLabelText('سباحة حرة'));
  fireEvent.click(within(a).getByLabelText('تشغيل مرافق السباحة في هذا الفرع'));
  fireEvent.click(within(a).getByRole('button',{name:'حفظ إعدادات الفرع'}));
  await screen.findByText('تم حفظ إعدادات فرع أ.');
  expect(within(b).getByLabelText('سباحة حرة')).toBeChecked();
  expect(within(screen.getByRole('heading',{name:'فرع أ'}).closest('form')!).getByLabelText('تشغيل مرافق السباحة في هذا الفرع')).not.toBeChecked();
});
it("does not show a late swimming response after switching back to football", async () => {
  let resolve!: (value: Response)=>void;
  const response=new Promise<Response>(r=>{resolve=r;});
  global.fetch=vi.fn(async input=>String(input).endsWith('/swimming')?response:Response.json(base));
  render(<SportOperationsSettings/>);
  fireEvent.change(await screen.findByLabelText('الرياضة'),{target:{value:'swim'}});
  fireEvent.change(screen.getByLabelText('الرياضة'),{target:{value:'football'}});
  resolve(Response.json({policies:[],history:[]}));
  await waitFor(()=>expect(screen.getByLabelText('نوع الرياضة')).toHaveValue('Football'));
  expect(screen.queryByRole('region',{name:'إعدادات السباحة'})).not.toBeInTheDocument();
});
it("hides settings if authorization is lost during readback and emits no success", async () => {
  let writes=0;
  global.fetch=vi.fn(async (input,init)=>{
    const url=String(input);
    if(url.endsWith('/csrf'))return Response.json({token:'csrf-test'});
    if(init?.method==='PUT'){writes++;return Response.json({});}
    if(url.endsWith('/swimming'))return writes?Response.json({}, {status:403}):Response.json({policies:[],history:[]});
    return Response.json(base);
  });
  render(<SportOperationsSettings/>);
  fireEvent.change(await screen.findByLabelText('الرياضة'),{target:{value:'swim'}});
  const form=(await screen.findByRole('heading',{name:'فرع أ'})).closest('form')!;
  fireEvent.click(within(form).getByRole('button',{name:'حفظ إعدادات الفرع'}));
  await screen.findByText(/تعذر تحميل إعدادات السباحة/);
  expect(screen.queryByLabelText('الرياضة')).not.toBeInTheDocument();
  expect(screen.queryByText('تم حفظ إعدادات فرع أ.')).not.toBeInTheDocument();
  expect(writes).toBe(1);
});
