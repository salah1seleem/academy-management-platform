import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import {afterEach,expect,it,vi} from 'vitest';
import {PlayerTechnicalReviews,TechnicalReviewDetail,TechnicalReviewInbox} from './player-technical-reviews';
vi.mock("next/navigation", () => ({
  usePathname: () => window.location.pathname,
  useSearchParams: () => new URL(window.location.href).searchParams,
}));
afterEach(() => {
  cleanup();
  vi.restoreAllMocks();
  window.history.replaceState(null, "", "/");
});
const request={id:'r',planId:'p',sportEnrollmentId:'e',sequence:1,sourceReviewSequence:5,levelName:'مستوى أول',curriculumTitle:'مهارات أصلية',requirements:2,actorName:'مدرب اصطناعي',createdAtUtc:'2026-10-18T07:00:00Z',version:7};
const overview={plan:{id:'p',version:3},enrollment:{player:'لاعب اصطناعي',sport:'كرة القدم'},source:{eligible:true,head:5,requirements:2,mastered:2},latestRequestId:null,canRequest:true,canDecide:false,items:[],hasMore:false};
it('family reads the actual separately applied level rather than a recommendation alone',async()=>{global.fetch=vi.fn(async p=>String(p).includes('/help')?Response.json({items:[]}):Response.json({...overview,items:[{request,decision:null,application:{toLevelName:'مستوى منفذ فعليًا',createdAtUtc:'2026-10-18T12:00:00Z'}}]})) as typeof fetch;render(<PlayerTechnicalReviews planId="p" playerId="child"/>);expect((await screen.findByText('مستوى منفذ فعليًا')).parentElement).toHaveTextContent('المستوى المنفّذ: مستوى منفذ فعليًا');expect(screen.queryByRole('button',{name:'اعتماد المستوى ونقل المجموعة'})).not.toBeInTheDocument();});
const detail={request,decision:null,enrollment:{id:'e',playerId:'child',player:'لاعب اصطناعي',sport:'كرة القدم'},evidence:[{name:'تحكم',unit:'Checklist',protocol:'نفس التمرين',state:'Mastered',measuredValue:null,observedOn:'2026-10-18',actorName:'مدرب اصطناعي'}],currentEligible:true,contextChanged:false,canDecide:true,levels:[{id:'next',name:'مستوى تالٍ'}]};
it('requests from the current server evidence and reuses intent after a lost response',async()=>{const commands:Record<string,unknown>[]=[];global.fetch=vi.fn(async(p,init)=>{if(String(p).endsWith('/csrf'))return Response.json({token:'csrf'});if(init?.method==='POST'){commands.push(JSON.parse(init.body as string));return commands.length===1?Response.json({},{status:503}):Response.json({id:'r'});}return Response.json(overview);}) as typeof fetch;render(<PlayerTechnicalReviews planId="p"/>);await screen.findByRole('button',{name:'طلب مراجعة فنية للمستوى'});fireEvent.click(screen.getByRole('button',{name:'طلب مراجعة فنية للمستوى'}));await screen.findByRole('alert');expect(screen.queryByText("تم إرسال طلب المراجعة.")).not.toBeInTheDocument();
  expect(screen.getByRole("button", {name:"طلب مراجعة فنية للمستوى"})).toBeDisabled();
  fireEvent.click(screen.getByRole("button", {name:"تحقّق من حالة المراجعة"}));
  await screen.findByRole("button", {name:"طلب مراجعة فنية للمستوى"});
  expect(commands).toHaveLength(1);
  fireEvent.click(screen.getByRole("button", {name:"طلب مراجعة فنية للمستوى"}));
  await waitFor(()=>expect(commands).toHaveLength(2));expect(commands[0]).toEqual(commands[1]);expect(commands[0]).toMatchObject({planVersion:3,sourceReviewSequence:5,previousRequestId:null,requestId:expect.any(String)});});
it('never exposes staff writes to family even if a malformed fixture says canRequest',async()=>{global.fetch=vi.fn(async p=>String(p).includes('/help')?Response.json({items:[]}):Response.json({...overview,items:[{request,decision:{outcome:'ReadyForPlacement',recommendedLevelName:'مستوى تالٍ',familyNote:'ملخص فني للأسرة',actorName:'مراجع مخول',createdAtUtc:'2026-10-18T08:00:00Z'}}]})) as typeof fetch;render(<PlayerTechnicalReviews planId="p" playerId="child"/>);await screen.findByText('ملخص فني للأسرة');expect(screen.queryByRole('button',{name:'طلب مراجعة فنية للمستوى'})).not.toBeInTheDocument();expect(screen.queryByRole('link',{name:'فتح الأدلة وقرار المراجعة'})).not.toBeInTheDocument();expect(screen.getByText(/لم يتغير مستوى اللاعب أو مجموعته/)).toBeInTheDocument();expect(global.fetch).toHaveBeenCalledWith('/api/v1/guardian/children/child/skills/plans/p/technical-reviews',expect.any(Object));});
it('readiness records a recommendation and family summary, not a level change',async()=>{const writes:{path:string;body:Record<string,unknown>}[]=[];global.fetch=vi.fn(async(p,init)=>{if(String(p).endsWith('/csrf'))return Response.json({token:'csrf'});if(init?.method==='POST'){writes.push({path:String(p),body:JSON.parse(init.body as string)});return Response.json({id:'d'});}return Response.json(detail);}) as typeof fetch;render(<TechnicalReviewDetail id="r"/>);fireEvent.change(await screen.findByRole('combobox',{name:'قرار المراجعة'}),{target:{value:'ReadyForPlacement'}});fireEvent.change(screen.getByRole('combobox',{name:'المستوى الموصى به'}),{target:{value:'next'}});fireEvent.change(screen.getByLabelText('ملخص فني للأسرة (اختياري)'),{target:{value:'خطوة فنية تالية'}});fireEvent.submit(screen.getByRole('button',{name:'حفظ قرار المراجعة ونشر الملخص'}).closest('form')!);await waitFor(()=>expect(writes).toHaveLength(1));expect(writes[0]).toMatchObject({path:'/api/v1/evaluations/technical-reviews/r/decision',body:{version:7,outcome:'ReadyForPlacement',recommendedLevelId:'next',familyNote:'خطوة فنية تالية'}});expect(writes.some(x=>x.path.includes('/placement-level'))).toBe(false);});
it('changed source offers closure rather than readiness; reader has no edit controls',async()=>{global.fetch=vi.fn(async()=>Response.json({...detail,currentEligible:false,contextChanged:true})) as typeof fetch;render(<TechnicalReviewDetail id="r"/>);await screen.findByRole('combobox',{name:'قرار المراجعة'});expect(screen.queryByRole('option',{name:'مستعد لمراجعة التسكين'})).not.toBeInTheDocument();expect(screen.getByRole('option',{name:'إغلاق لتغير الخطة أو الأدلة'})).toBeInTheDocument();expect(screen.getByRole('alert')).toHaveTextContent('تغيّرت الخطة');});
it('inbox shows saved child context and opens selected source; include-decided changes the real query',async()=>{const paths:string[]=[];global.fetch=vi.fn(async p=>{paths.push(String(p));return Response.json({items:[{request,decision:null}],contexts:{e:{player:'لاعب اصطناعي',branch:'فرع اصطناعي',sport:'كرة القدم'}},hasMore:false});}) as typeof fetch;const {rerender}=render(<TechnicalReviewInbox/>);expect(await screen.findByRole('link',{name:'مراجعة الأدلة والقرار'})).toHaveAttribute('href','/dashboard/evaluations/technical-reviews/r');fireEvent.click(screen.getByRole('checkbox'));
  expect(window.location.search).toBe("?includeDecided=true");
  rerender(<TechnicalReviewInbox/>);
  expect(await screen.findByRole("link",{name:"مراجعة الأدلة والقرار"}))
    .toHaveAttribute("href","/dashboard/evaluations/technical-reviews/r?includeDecided=true");
  expect(paths).toContain("/api/v1/evaluations/technical-reviews/inbox?includeDecided=true");
});
it('live revocation removes request evidence rather than retaining stale data',async()=>{let allowed=true;global.fetch=vi.fn(async()=>allowed?Response.json(detail):Response.json({},{status:404})) as typeof fetch;render(<TechnicalReviewDetail id="r"/>);await screen.findByText('نفس التمرين');allowed=false;fireEvent.focus(window);await waitFor(()=>expect(screen.queryByText('نفس التمرين')).not.toBeInTheDocument());await screen.findByText('طلب المراجعة غير متاح لك الآن.');});

it("checks the saved decision after a lost response with no second POST", async () => {
  let posts = 0;
  const saved = { outcome: "NeedsPractice", familyNote: "يكرر التمرين", recommendedLevelName: null, actorName: "مراجع", createdAtUtc: "2026-11-08T09:00:00Z" };
  global.fetch = vi.fn(async (path, init) => {
    if (String(path).endsWith("/csrf")) return Response.json({ token: "csrf" });
    if (init?.method === "POST") {
      posts++;
      throw new TypeError("network request failed");
    }
    return Response.json(posts ? { ...detail, decision: saved, canDecide: false } : detail);
  }) as typeof fetch;
  render(<TechnicalReviewDetail id="r" />);
  const select = await screen.findByRole("combobox", { name: "قرار المراجعة" });
  const save = screen.getByRole("button", { name: "حفظ قرار المراجعة ونشر الملخص" });
  expect(save).toBeDisabled();
  fireEvent.change(select, { target: { value: "NeedsPractice" } });
  fireEvent.click(save);
  const check = await screen.findByRole("button", { name: "تحقّق من حالة المراجعة" });
  expect(save).toBeDisabled();
  fireEvent.click(check);
  expect(await screen.findByText(saved.familyNote)).toBeVisible();
  expect(screen.queryByRole("button", { name: "حفظ قرار المراجعة ونشر الملخص" })).toBeNull();
  expect(posts).toBe(1);
});

it("keeps the note and asks for a new choice when readiness is invalidated", async () => {
  let allowed = true;
  global.fetch = vi.fn(async () => Response.json(allowed ? detail
    : { ...detail, currentEligible: false, contextChanged: true, levels: [] })) as typeof fetch;
  render(<TechnicalReviewDetail id="r" />);
  const select = await screen.findByRole("combobox", { name: "قرار المراجعة" });
  fireEvent.change(select, { target: { value: "ReadyForPlacement" } });
  fireEvent.change(screen.getByRole("combobox", { name: "المستوى الموصى به" }), { target: { value: "next" } });
  fireEvent.change(screen.getByLabelText("ملخص فني للأسرة (اختياري)"), { target: { value: "مسودة المراجع" } });
  allowed = false;
  fireEvent.focus(window);
  expect(await screen.findByRole("option", { name: "إغلاق لتغير الخطة أو الأدلة" })).toBeVisible();
  expect(screen.getByRole("combobox", { name: "قرار المراجعة" })).toHaveValue("");
  expect(screen.getByLabelText("ملخص فني للأسرة (اختياري)")).toHaveValue("مسودة المراجع");
  expect(screen.getByRole("button", { name: "حفظ قرار المراجعة ونشر الملخص" })).toBeDisabled();
});
