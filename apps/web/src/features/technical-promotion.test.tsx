import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import {afterEach,expect,it,vi} from 'vitest';
import {TechnicalPromotion} from './technical-promotion';
afterEach(()=>{cleanup();vi.restoreAllMocks();});
const options={player:'لاعب اصطناعي',sport:'كرة القدم',branch:'فرع العرض',enrollmentVersion:9,effectiveDate:'2026-10-18',recommendedLevel:'مستوى تالٍ',canPreview:true,applied:null,groups:[{id:'g',name:'مجموعة متقدمة',capacity:1}],hasMore:false};
const preview={fingerprint:'fp',enrollmentVersion:9,level:'مستوى تالٍ',preview:{sourceGroup:'مجموعة أصلية',targetGroup:'مجموعة متقدمة',effectiveDate:'2026-10-18',conflicts:[],removedSessions:[],addedSessions:[{id:'s',sessionDate:'2026-10-19',startTime:'07:00:00',endTime:'08:00:00'}]}};
it('requires a server preview then a distinct explicit atomic confirmation with source version',async()=>{const writes:{path:string;body:Record<string,unknown>}[]=[];let applied=false;global.fetch=vi.fn(async(p,init)=>{if(String(p).endsWith('/csrf'))return Response.json({token:'c'});if(init?.method==='POST'){writes.push({path:String(p),body:JSON.parse(init.body as string)});if(String(p).endsWith('/preview'))return Response.json(preview);applied=true;return Response.json({id:'h'});}return Response.json({...options,applied:applied?{id:'h',toLevelName:'مستوى تالٍ',createdAtUtc:'2026-10-18T12:00:00Z',effectiveDate:'2026-10-17'}:null});}) as typeof fetch;render(<TechnicalPromotion id="r"/>);expect(screen.queryByRole('button',{name:'اعتماد المستوى ونقل المجموعة'})).not.toBeInTheDocument();fireEvent.change(await screen.findByRole('combobox'),{target:{value:'g'}});fireEvent.click(screen.getByRole('button',{name:'معاينة المستوى والمواعيد'}));fireEvent.click(await screen.findByRole('button',{name:'اعتماد المستوى ونقل المجموعة'}));await waitFor(()=>expect(screen.getByRole('status')).toHaveTextContent('تم التنفيذ إلى مستوى تالٍ'));expect(screen.getByText(/بداية النقل · 17 أكتوبر/)).toBeVisible();expect(writes).toHaveLength(2);expect(writes[0]).toMatchObject({path:'/api/v1/evaluations/technical-reviews/r/promotion/preview',body:{groupId:'g',enrollmentVersion:9,fingerprint:null}});expect(writes[1]).toMatchObject({path:'/api/v1/evaluations/technical-reviews/r/promotion',body:{groupId:'g',enrollmentVersion:9,fingerprint:'fp'}});expect(screen.queryByRole('button',{name:'اعتماد المستوى ونقل المجموعة'})).not.toBeInTheDocument();});
it('reads first after a lost response and requires a fresh preview before reusing the same intent', async () => {
  const commands: Record<string, unknown>[] = [];
  let previews = 0;
  global.fetch = vi.fn(async (path, init) => {
    if (String(path).endsWith('/csrf')) return Response.json({token:'c'});
    if (String(path).endsWith('/preview')) { previews++; return Response.json(preview); }
    if (init?.method === 'POST') { commands.push(JSON.parse(init.body as string)); return Response.json({}, {status:503}); }
    return Response.json(options);
  }) as typeof fetch;
  render(<TechnicalPromotion id="r"/>);
  fireEvent.change(await screen.findByRole('combobox'), {target:{value:'g'}});
  const openPreview = async () => {
    fireEvent.click(await screen.findByRole('button',{name:'معاينة المستوى والمواعيد'}));
    return screen.findByRole('button',{name:'اعتماد المستوى ونقل المجموعة'});
  };
  fireEvent.click(await openPreview());
  fireEvent.click(await screen.findByRole('button', {name:'تحقّق من سجل التنفيذ'}));
  expect(await screen.findByRole('button', {name:'معاينة المستوى والمواعيد'})).toBeEnabled();
  expect(screen.getByText('لا يوجد تنفيذ مسجّل لهذه التوصية.')).toBeVisible();
  expect(commands).toHaveLength(1);
  expect(screen.queryByRole('button',{name:'اعتماد المستوى ونقل المجموعة'})).toBeNull();
  fireEvent.click(await openPreview());
  await waitFor(() => expect(commands).toHaveLength(2));
  expect(previews).toBe(2);
  expect(commands[0]).toEqual(commands[1]);
});

it('shows the committed record after a lost response without a second POST', async () => {
  let saves = 0;
  global.fetch = vi.fn(async (path, init) => {
    if (String(path).endsWith('/csrf')) return Response.json({token:'c'});
    if (String(path).endsWith('/preview')) return Response.json(preview);
    if (init?.method === 'POST') { saves++; throw new TypeError('network failed'); }
    return Response.json({...options, applied:saves ? {id:'h',toLevelName:'مستوى تالٍ',effectiveDate:'2026-10-17'} : null});
  }) as typeof fetch;
  render(<TechnicalPromotion id="r"/>);
  fireEvent.change(await screen.findByRole('combobox'), {target:{value:'g'}});
  fireEvent.click(screen.getByRole('button',{name:'معاينة المستوى والمواعيد'}));
  expect(await screen.findByText(/19 أكتوبر/)).toBeVisible();
  fireEvent.click(await screen.findByRole('button',{name:'اعتماد المستوى ونقل المجموعة'}));
  expect(screen.queryByRole('status')).toBeNull();
  fireEvent.click(await screen.findByRole('button',{name:'تحقّق من سجل التنفيذ'}));
  await waitFor(()=>expect(screen.getByRole('status')).toHaveTextContent('تم التنفيذ إلى مستوى تالٍ'));
  expect(screen.queryByRole('combobox')).toBeNull();
  expect(saves).toBe(1);
});
it('does not allow committing a capacity conflict or a changed target without re-preview',async()=>{global.fetch=vi.fn(async(p)=>Response.json(String(p).endsWith('/csrf')?{token:'c'}:String(p).endsWith('/preview')?{...preview,preview:{...preview.preview,conflicts:['السعة لا تكفي']}}:options)) as typeof fetch;render(<TechnicalPromotion id="r"/>);fireEvent.change(await screen.findByRole('combobox'),{target:{value:'g'}});fireEvent.click(screen.getByRole('button',{name:'معاينة المستوى والمواعيد'}));expect(await screen.findByRole('button',{name:'اعتماد المستوى ونقل المجموعة'})).toBeDisabled();fireEvent.change(screen.getByRole('combobox'),{target:{value:''}});expect(screen.queryByRole('button',{name:'اعتماد المستوى ونقل المجموعة'})).not.toBeInTheDocument();});
it('removes the private preview on authority revocation',async()=>{let allowed=true;global.fetch=vi.fn(async()=>allowed?Response.json(options):Response.json({},{status:403})) as typeof fetch;render(<TechnicalPromotion id="r"/>);await screen.findByRole('heading',{name:'لاعب اصطناعي'});allowed=false;fireEvent.focus(window);await waitFor(()=>expect(screen.queryByRole('heading',{name:'لاعب اصطناعي'})).not.toBeInTheDocument());await screen.findByRole('alert');});

it('requires another preview after a new read even when the enrollment version is unchanged', async () => {
  global.fetch = vi.fn(async (path) => Response.json(String(path).endsWith('/csrf') ? {token:'c'}
    : String(path).endsWith('/preview') ? preview : options)) as typeof fetch;
  render(<TechnicalPromotion id="r"/>);
  fireEvent.change(await screen.findByRole('combobox'), {target:{value:'g'}});
  fireEvent.click(screen.getByRole('button',{name:'معاينة المستوى والمواعيد'}));
  expect(await screen.findByRole('button',{name:'اعتماد المستوى ونقل المجموعة'})).toBeEnabled();
  fireEvent.focus(window);
  expect(await screen.findByRole('button',{name:'معاينة المستوى والمواعيد'})).toBeEnabled();
  expect(screen.queryByRole('button',{name:'اعتماد المستوى ونقل المجموعة'})).toBeNull();
});
