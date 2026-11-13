import { cleanup,fireEvent,render,screen,waitFor } from '@testing-library/react';
import { afterEach,expect,it,vi } from 'vitest';
import { PlayerSkillJourney,PlayerSkillPlan,SkillCurriculumEditor,GuardianPlayerSkills } from './player-skills';
const {push}=vi.hoisted(()=>({push:vi.fn()}));vi.mock('next/navigation',()=>({usePathname:()=>'/guardian/children/p',useSearchParams:()=>new URLSearchParams(),useRouter:()=>({push})}));
afterEach(()=>{cleanup();vi.restoreAllMocks();push.mockClear();});
const enrollment={id:'e',playerId:'child',player:'لاعب اصطناعي',sport:'كرة القدم',level:'مستوى أول'};
const curriculum={id:'c',trainingLevelId:'level',title:'منهج خطوات الكرة',levelNameSnapshot:'مستوى أول',revisionNumber:1,version:7,state:'Published'};
const requirement={id:'r',name:'تحكم بالكرة',unit:'Checklist',protocol:'ملاحظة من نفس التمرين'};
function plan(canReview=false){return {enrollment,plan:{id:'p',version:9,assignedOn:'2026-10-18',curriculum},asOfDate:'2026-10-18',contextChanged:false,canReview,progress:{mastered:0,assessed:0,total:2,percentage:0,sourcePercentage:0,readyForTechnicalReview:false},skills:[{requirement,latest:null,baseline:null}],history:[],historyHasMore:false};}
it('keeps the exact child sport and current version when assigning a plan',async()=>{
 const commands:unknown[]=[];global.fetch=vi.fn(async(path,init)=>{if(String(path).endsWith('/csrf'))return Response.json({token:'csrf'});if(init?.method==='POST'){commands.push(JSON.parse(init.body as string));return Response.json({id:'p'});}return Response.json({enrollment,canAssign:true,availableCurricula:[curriculum],active:null,history:[]});}) as typeof fetch;
 render(<PlayerSkillJourney enrollmentId="e"/>);await screen.findByText('لاعب اصطناعي · كرة القدم');fireEvent.change(screen.getByRole('combobox'),{target:{value:'c'}});fireEvent.click(screen.getByRole('button',{name:'إسناد خطة المهارات'}));await screen.findByText('تم إسناد الخطة مع حفظ السجل السابق.');expect(commands).toEqual([expect.objectContaining({curriculumId:'c',previousPlanId:null,previousVersion:0})]);expect(await screen.findByRole('link',{name:'ملف اللاعب ومراجعة مستواه'})).toHaveAttribute('href','/dashboard/players/child?tab=training');
});
it('publishes a checklist observation with null measurement and same request ID on retry',async()=>{
 const commands:Record<string,unknown>[]=[];global.fetch=vi.fn(async(path,init)=>{if(String(path).endsWith('/csrf'))return Response.json({token:'csrf'});if(init?.method==='POST'){expect(String(path)).toBe('/api/v1/evaluations/skills/plans/p/reviews');commands.push(JSON.parse(init.body as string));return commands.length===1?Response.json({message:'تعذر تأكيد الحفظ'},{status:503}):Response.json({id:'review'});}return Response.json(plan(true));}) as typeof fetch;
 render(<PlayerSkillPlan id="p"/>);await screen.findByLabelText('المهارة');fireEvent.change(screen.getByLabelText('المهارة'),{target:{value:'r'}});fireEvent.change(screen.getByLabelText('ملاحظة للأسرة (اختياري)'),{target:{value:'مراجعة حقيقية'}});fireEvent.click(screen.getByRole('button',{name:'حفظ ونشر الملاحظة للأسرة'}));await screen.findByText('تعذر تأكيد الحفظ');fireEvent.click(screen.getByRole('button',{name:'حفظ ونشر الملاحظة للأسرة'}));await screen.findByText('تم حفظ الملاحظة وأصبحت متاحة لولي الأمر.');expect(commands).toHaveLength(2);expect(commands[0]).toEqual(commands[1]);expect(commands[0]).toMatchObject({requirementId:'r',state:'Learning',measuredValue:null,observedOn:'2026-10-18',note:'مراجعة حقيقية'});expect(commands[0].requestId).toMatch(/^[a-f0-9-]{36}$/);
});
it('does not open the previous plan while replacement is pending or not yet revalidated',async()=>{
 let resolve:(r:Response)=>void=()=>{};let changed=false;vi.spyOn(window,'confirm').mockReturnValue(true);
 global.fetch=vi.fn(async(p,init)=>{if(String(p).endsWith('/csrf'))return Response.json({token:'csrf'});if(init?.method==='POST')return new Promise<Response>(r=>{resolve=response=>{changed=true;r(response);};});return Response.json({enrollment,canAssign:true,availableCurricula:[{...curriculum,id:'newc'}],active:{...plan(),plan:{...plan().plan,id:changed?'newp':'oldp'}},history:[]});}) as typeof fetch;
 render(<PlayerSkillJourney enrollmentId="e"/>);expect(await screen.findByRole('link',{name:'فتح المهارات والملاحظات'})).toHaveAttribute('href','/dashboard/evaluations/skills/plans/oldp');fireEvent.change(screen.getByRole('combobox'),{target:{value:'newc'}});fireEvent.click(screen.getByRole('button',{name:'استبدال الخطة مع حفظ التاريخ'}));await waitFor(()=>expect(screen.queryByRole('link',{name:'فتح المهارات والملاحظات'})).not.toBeInTheDocument());resolve(Response.json({id:'newp'}));expect(await screen.findByRole('link',{name:'فتح المهارات والملاحظات'})).toHaveAttribute('href','/dashboard/evaluations/skills/plans/newp');
});
it('retains null assessment, never implies automatic promotion and removes private content on revocation',async()=>{
 let allowed=true;global.fetch=vi.fn(async()=>allowed?Response.json(plan()):Response.json({},{status:404})) as typeof fetch;render(<PlayerSkillPlan id="p"/>);await screen.findByText('تحكم بالكرة');expect(screen.getByText('لم يُقيّم')).toBeVisible();expect(screen.queryByRole('button',{name:'حفظ ونشر الملاحظة للأسرة'})).not.toBeInTheDocument();allowed=false;fireEvent.focus(window);await waitFor(()=>expect(screen.queryByText('تحكم بالكرة')).not.toBeInTheDocument());await screen.findByText(/خطة المهارات أو اللاعب غير متاح/);
});
it('opens a linked family plan without assignment or staff controls',async()=>{
 global.fetch=vi.fn(async p=>String(p).includes('/help')?Response.json({items:[]}):Response.json({items:[{enrollment,active:plan(),history:[],canAssign:false}]})) as typeof fetch;render(<GuardianPlayerSkills playerId="child"/>);expect(await screen.findByRole('link',{name:'مهارات كرة القدم وملاحظات المدرب'})).toHaveAttribute('href','/guardian/children/child/skills/plans/p');expect(screen.queryByRole('button',{name:'إسناد خطة المهارات'})).not.toBeInTheDocument();
});
it('published definitions are immutable and copying navigates to the new draft',async()=>{
 global.fetch=vi.fn(async(p,init)=>{if(String(p).endsWith('/csrf'))return Response.json({token:'csrf'});if(init?.method==='POST'){expect(String(p)).toBe('/api/v1/evaluations/skills/curricula');expect(JSON.parse(init.body as string)).toMatchObject({copiedFromId:'c',sourceVersion:7,requirements:[{name:requirement.name,unit:'Checklist',protocol:requirement.protocol}]});return Response.json({id:'copy'});}return Response.json(String(p).endsWith('/options')?{canCreate:true,canEdit:true,levels:[],sports:[]}:{item:curriculum,requirements:[requirement],canEdit:true,canCopy:true});}) as typeof fetch;
 render(<SkillCurriculumEditor id="c"/>);await screen.findByText(curriculum.title);expect(screen.queryByLabelText('اسم نسخة المنهج')).not.toBeInTheDocument();fireEvent.click(screen.getByRole('button',{name:'نسخ إلى مسودة جديدة'}));await waitFor(()=>expect(push).toHaveBeenCalledWith('/dashboard/evaluations/skills/curricula/copy'));
});

it('distinguishes an unreviewed plan from measured zero and labels old evidence', async () => {
  const initial = plan();
  const review = { id:"r1", requirementId:"r", state:"Learning", measuredValue:0, note:"ابدأ من مسافة أقصر", observedOn:"2026-10-18", actorName:"مدرب العرض" };
  let response: object = initial;
  global.fetch = vi.fn(async path => Response.json(String(path).includes("/help") ? {items:[]} : response)) as typeof fetch;
  render(<PlayerSkillPlan id="p" playerId="child"/>);
  await screen.findByText("لم تُراجع أي مهارة بعد.");
  expect(screen.queryByRole("progressbar")).not.toBeInTheDocument();
  expect(screen.queryByText("عرض السجل")).not.toBeInTheDocument();

  const reviewed = { ...initial, progress:{...initial.progress, assessed:1}, skills:[{requirement:{...requirement,unit:"Count"},latest:review,baseline:review}], history:[review] };
  response = reviewed;
  fireEvent.focus(window);
  expect(await screen.findByRole("progressbar")).toHaveAttribute("value", "0");
  expect(screen.getAllByText(review.note)[0]).toBeVisible();
  expect(screen.getByText("عرض السجل")).toBeVisible();
  expect(screen.getAllByText("مدرب العرض · 18 أكتوبر 2026")[0]).toBeVisible();

  response = {...reviewed, contextChanged:true, progress:{...reviewed.progress, percentage:null}};
  fireEvent.focus(window);
  await screen.findByText("خطة غير سارية");
  expect(screen.queryByRole("progressbar")).not.toBeInTheDocument();
  expect(screen.getAllByText(review.note)[0]).toBeVisible();
});

it('keeps the review draft on refresh and only sends zero when entered', async () => {
  const current = plan(true);
  const response = {...current, skills:[{...current.skills[0], requirement:{...requirement,unit:"Count"}}]};
  const commands: unknown[] = [];
  global.fetch = vi.fn(async (path, init) => {
    if (String(path).endsWith("/csrf")) return Response.json({token:"csrf"});
    if (init?.method === "POST") { commands.push(JSON.parse(init.body as string)); return Response.json({id:"review"}); }
    return Response.json(response);
  }) as typeof fetch;
  render(<PlayerSkillPlan id="p"/>);
  fireEvent.change(await screen.findByLabelText("المهارة"), {target:{value:"r"}});
  const note = screen.getByLabelText("ملاحظة للأسرة (اختياري)");
  fireEvent.change(note, {target:{value:"نراجع التمرين الأسبوع المقبل"}});
  fireEvent.submit(screen.getByRole("form"));
  expect(commands).toHaveLength(0);
  fireEvent.change(screen.getByRole("spinbutton"), {target:{value:"0"}});
  fireEvent.focus(window);
  expect(await screen.findByRole("spinbutton")).toHaveValue(0);
  expect(screen.getByLabelText("ملاحظة للأسرة (اختياري)")).toHaveValue("نراجع التمرين الأسبوع المقبل");
  fireEvent.click(screen.getByRole("button", {name:"حفظ ونشر الملاحظة للأسرة"}));
  await screen.findByText("تم حفظ الملاحظة وأصبحت متاحة لولي الأمر.");
  expect(commands).toEqual([expect.objectContaining({requirementId:"r", measuredValue:0, note:"نراجع التمرين الأسبوع المقبل"})]);
});

it('requires saving the displayed draft before publishing and confirms the refreshed version', async () => {
  let current = {...curriculum, state:"Draft"};
  const calls: {path:string,body:Record<string,unknown>}[] = [];
  global.fetch = vi.fn(async (path, init) => {
    if (String(path).endsWith("/csrf")) return Response.json({token:"csrf"});
    if (init?.method === "PUT" || init?.method === "POST") {
      const body = JSON.parse(init.body as string);
      calls.push({path:String(path),body});
      current = {...current, version:current.version + 1, ...(init.method === "PUT" ? {title:body.title} : {state:"Published"})};
      return Response.json({id:current.id});
    }
    return Response.json({item:current,requirements:[requirement],canEdit:true,canCopy:true});
  }) as typeof fetch;
  render(<SkillCurriculumEditor id="c"/>);
  const title = await screen.findByLabelText("اسم نسخة المنهج");
  fireEvent.change(title,{target:{value:"خطة السباحة المحدثة"}});
  expect(screen.getByRole("button",{name:"نشر نسخة المنهج"})).toBeDisabled();
  fireEvent.focus(window);
  expect(await screen.findByLabelText("اسم نسخة المنهج")).toHaveValue("خطة السباحة المحدثة");
  fireEvent.click(screen.getByRole("button",{name:"حفظ المسودة"}));
  await screen.findByRole("heading",{name:"خطة السباحة المحدثة"});
  fireEvent.click(screen.getByRole("button",{name:"نشر نسخة المنهج"}));
  const dialog = screen.getByRole("dialog");
  expect(dialog).toHaveTextContent("تبقى خطط اللاعبين المسندة على نسخها الحالية.");
  expect(calls).toHaveLength(1);
  fireEvent.click(screen.getByRole("button",{name:"تأكيد النشر"}));
  await screen.findByText("منشورة");
  expect(calls[1]).toMatchObject({path:"/api/v1/evaluations/skills/curricula/c/publish",body:{version:8}});
  expect(screen.queryByLabelText("اسم نسخة المنهج")).not.toBeInTheDocument();
});

it('recovers the current curriculum with a read after an uncertain stop response', async () => {
  let current = {...curriculum};
  let writes = 0;
  global.fetch = vi.fn(async (path, init) => {
    if (String(path).endsWith("/csrf")) return Response.json({token:"csrf"});
    if (init?.method === "POST") {
      writes++;
      current = {...current,state:"Retired",version:8};
      return Response.json({}, {status:503});
    }
    return Response.json({item:current,requirements:[requirement],canEdit:true,canCopy:true});
  }) as typeof fetch;
  render(<SkillCurriculumEditor id="c"/>);
  fireEvent.click(await screen.findByRole("button",{name:"إيقاف النسخة"}));
  expect(screen.getByRole("dialog")).toHaveTextContent("تبقى المراجعات السابقة محفوظة.");
  fireEvent.click(screen.getByRole("button",{name:"إلغاء"}));
  expect(writes).toBe(0);
  fireEvent.click(screen.getByRole("button",{name:"إيقاف النسخة"}));
  fireEvent.click(screen.getByRole("button",{name:"تأكيد الإيقاف"}));
  fireEvent.click(await screen.findByRole("button",{name:"تحديث بيانات النسخة"}));
  await screen.findByText("موقوفة");
  expect(writes).toBe(1);
  expect(screen.queryByRole("button",{name:"إيقاف النسخة"})).not.toBeInTheDocument();
});

it('retries a copy using the same request even if the source changes', async () => {
  let source = {...curriculum,title:"أ".repeat(100)};
  const writes: unknown[] = [];
  global.fetch = vi.fn(async (path, init) => {
    if (String(path).endsWith("/csrf")) return Response.json({token:"csrf"});
    if (init?.method === "POST") {
      writes.push(JSON.parse(init.body as string));
      return writes.length === 1 ? Response.json({}, {status:503}) : Response.json({id:"copy"});
    }
    return Response.json({item:source,requirements:[requirement],canEdit:true,canCopy:true});
  }) as typeof fetch;
  render(<SkillCurriculumEditor id="c"/>);
  fireEvent.click(await screen.findByRole("button",{name:"نسخ إلى مسودة جديدة"}));
  await screen.findByRole("button",{name:"إعادة محاولة الطلب نفسه"});
  source = {...source,version:8,state:"Retired"};
  fireEvent.focus(window);
  await screen.findByText("موقوفة");
  expect(screen.getByRole("button",{name:"نسخ إلى مسودة جديدة"})).toBeDisabled();
  fireEvent.click(screen.getByRole("button",{name:"إعادة محاولة الطلب نفسه"}));
  await waitFor(()=>expect(push).toHaveBeenCalledWith("/dashboard/evaluations/skills/curricula/copy"));
  expect(writes).toHaveLength(2);
  expect(writes[1]).toEqual(writes[0]);
  expect(writes[0]).toMatchObject({title:source.title,sourceVersion:7});
});

it('requires accepting the current draft before editing after a version change', async () => {
  let current = {...curriculum,state:"Draft"};
  const read = vi.fn(async () => Response.json({item:current,requirements:[requirement],canEdit:true,canCopy:true}));
  global.fetch = read as typeof fetch;
  render(<SkillCurriculumEditor id="c"/>);
  fireEvent.change(await screen.findByLabelText("اسم نسخة المنهج"), {target:{value:"تعديل محلي"}});
  current = {...current,version:8,title:"مسودة عدّلها زميل"};
  fireEvent.focus(window);
  await screen.findByText(/تغيّرت المسودة المحفوظة/);
  expect(screen.getByLabelText("اسم نسخة المنهج")).toHaveValue("تعديل محلي");
  expect(screen.getByRole("button",{name:"حفظ المسودة"})).toBeDisabled();
  fireEvent.click(screen.getByRole("button",{name:"استخدام النسخة الحالية"}));
  expect(screen.getByLabelText("اسم نسخة المنهج")).toHaveValue("مسودة عدّلها زميل");
  expect(screen.getByRole("button",{name:"نشر نسخة المنهج"})).toBeEnabled();
});
