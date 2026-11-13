import{cleanup,fireEvent,render,screen,waitFor}from'@testing-library/react';
import{afterEach,expect,it,vi}from'vitest';
import{GroupMediaAudienceFields,GroupMediaConsent,MediaWithdrawal}from'./group-media-privacy';
import{MediaUpload}from'./media-upload';
afterEach(()=>{cleanup();vi.restoreAllMocks();});
const choice={player:'طفل اصطناعي',allowed:false,revision:0,notice:'مشاركة المجموعة مستقلة عن الإعلان ويمكن سحبها.'};
it('choice is off until explicitly saved; a failed response preserves intent and the server confirms the saved choice',async()=>{
 const writes:unknown[]=[];let saved=false;
 global.fetch=vi.fn(async(p,o)=>String(p).endsWith('/csrf')?Response.json({token:'t'}):o?.method==='POST'?(writes.push(JSON.parse(o.body as string)),saved=writes.length===2,Response.json({},{status:saved?200:503})):Response.json({...choice,allowed:saved,revision:saved?1:0}))as typeof fetch;
 render(<GroupMediaConsent playerId="p"/>);await screen.findByText('طفل اصطناعي · المشاركة غير مسموحة');expect(writes).toHaveLength(0);
 fireEvent.click(screen.getByRole('button',{name:'السماح بالمشاركة داخل المجموعة'}));await screen.findByRole('alert');expect(screen.queryByText('طفل اصطناعي · المشاركة مسموحة باختيارك')).not.toBeInTheDocument();
 fireEvent.click(screen.getByRole('button',{name:'السماح بالمشاركة داخل المجموعة'}));await screen.findByText('طفل اصطناعي · المشاركة مسموحة باختيارك');expect(writes[0]).toEqual(writes[1]);expect(writes[0]).toMatchObject({allowed:true,revision:0});
});
it('unlinking on focus clears the child choice instead of retaining a private preference',async()=>{
 let allowed=true;global.fetch=vi.fn(async()=>allowed?Response.json(choice):Response.json({},{status:404}))as typeof fetch;render(<GroupMediaConsent playerId="p"/>);await screen.findByText('طفل اصطناعي · المشاركة غير مسموحة');allowed=false;fireEvent.focus(window);await screen.findByRole('alert');expect(screen.queryByText(/طفل اصطناعي/)).not.toBeInTheDocument();expect(screen.queryByRole('button')).not.toBeInTheDocument();
});
const audience={eventOn:'2026-10-18',asOfDate:'2026-10-18',notice:'حدد الظاهرين بعد المراجعة',players:[{id:'p',name:'طفل موافق',consented:true},{id:'x',name:'طفل بلا موافقة',consented:false}]};
for(const status of [400,503])it(`content inspection ${status} keeps the reviewed draft and never reports publication`,async()=>{
 let writes=0;global.fetch=vi.fn(async(p)=>{const path=String(p);if(path.endsWith('/csrf'))return Response.json({token:'t'});if(path.endsWith('/upload')){writes++;return Response.json({message:status===503?'فحص الملفات غير متاح؛ لم يُنشر الملف.':'الملف تالف؛ لم يُنشر الملف.',...(status===503?{code:'media_inspection_unavailable'}:{})},{status});}if(path.includes('media-audience'))return Response.json(audience);if(path.includes('/assets/media-groups'))return Response.json({items:[{id:'g',arabicName:'مجموعة اختبار'}],hasMore:false});return Response.json({items:[],hasMore:false});})as typeof fetch;
 render(<MediaUpload/>);fireEvent.change(screen.getByRole('combobox',{name:'النشر إلى'}),{target:{value:'group'}});await screen.findByRole('option',{name:'مجموعة اختبار'});fireEvent.change(screen.getByRole('combobox',{name:/^المجموعة$/}),{target:{value:'g'}});fireEvent.click(await screen.findByRole('checkbox',{name:/طفل موافق/}));expect(screen.getByText('الصيغ وحدود الرفع')).toBeVisible();fireEvent.change(screen.getByLabelText('الصورة أو الفيديو'),{target:{files:[new File(['synthetic'],'pixel.png',{type:'image/png'})]}});fireEvent.change(screen.getByLabelText('الوصف · اختياري'),{target:{value:'مسودة مراجعة اصطناعية'}});fireEvent.click(screen.getByRole('checkbox',{name:/راجعت الملف/}));fireEvent.submit(screen.getByRole('button',{name:'رفع ونشر'}).closest('form')!);await screen.findByRole('alert');expect(writes).toBe(1);expect(screen.getByLabelText('الوصف · اختياري')).toHaveValue('مسودة مراجعة اصطناعية');expect(screen.getByRole('checkbox',{name:/راجعت الملف/})).toBeChecked();expect(screen.queryByText(/تم نشر النسخة/)).not.toBeInTheDocument();
});
it('session selection rereads the authorized roster and clears the previous subject decision',async()=>{
 const requests:string[]=[];const changed=vi.fn();global.fetch=vi.fn(async p=>{requests.push(String(p));return Response.json({...audience,sessions:[{id:'s',startTime:'15:00:00',endTime:'16:00:00'}],players:String(p).includes('sessionId=s')?[{id:'v',name:'ضيف الحصة',consented:true}]:audience.players});})as typeof fetch;
 render(<form><GroupMediaAudienceFields groupId="g" onReviewChanged={changed}/></form>);const p=await screen.findByRole('checkbox',{name:/طفل موافق/});fireEvent.click(p);expect(new FormData(p.closest('form')!).get('subjectPlayerIds')).toBe('p');fireEvent.change(screen.getByRole('combobox',{name:'الحصة · اختياري'}),{target:{value:'s'}});const v=await screen.findByRole('checkbox',{name:/ضيف الحصة/});expect(v).not.toBeChecked();expect(new FormData(v.closest('form')!).get('sessionId')).toBe('s');expect(new FormData(v.closest('form')!).get('subjectPlayerIds')).toBe('');expect(screen.queryByRole('checkbox',{name:/طفل موافق/})).not.toBeInTheDocument();expect(requests.some(p=>p.includes('sessionId=s'))).toBe(true);expect(changed).toHaveBeenCalledTimes(2);
});
it('withdrawal needs explicit confirmation; note is optional and a lost response keeps the same request without false success',async()=>{
 const writes:unknown[]=[];const saved=vi.fn();global.fetch=vi.fn(async(p,o)=>String(p).endsWith('/csrf')?Response.json({token:'t'}):(writes.push(JSON.parse(o?.body as string)),Response.json({},{status:writes.length===1?503:200})))as typeof fetch;
 render(<MediaWithdrawal id="a" onSaved={saved}/>);fireEvent.click(screen.getByRole('button',{name:'سحب النشر'}));expect(writes).toHaveLength(0);fireEvent.click(screen.getByRole('button',{name:'رجوع'}));expect(writes).toHaveLength(0);fireEvent.click(screen.getByRole('button',{name:'سحب النشر'}));fireEvent.click(screen.getByRole('button',{name:'تأكيد سحب النشر'}));await screen.findByRole('alert');expect(saved).not.toHaveBeenCalled();fireEvent.click(screen.getByRole('button',{name:'تأكيد سحب النشر'}));await waitFor(()=>expect(saved).toHaveBeenCalledOnce());expect(writes[0]).toEqual(writes[1]);expect(writes[0]).toMatchObject({note:null});
});
it('administrative gallery rereads withdrawal status, never pretends restore, and hides records when authorization is lost',async()=>{
 let withdrawn=false,accessible=true;global.fetch=vi.fn(async(p)=>{const path=String(p);if(path.endsWith('/csrf'))return Response.json({token:'t'});if(path.endsWith('/withdraw')){withdrawn=true;return Response.json({});}if(path.includes('/assets/gallery'))return accessible?Response.json([{id:'a',caption:'نسخة اصطناعية',url:'/api/v1/assets/a',contentType:'image/png',trainingGroupId:'g',withdrawn,canWithdraw:true,canPublish:true}]):Response.json({},{status:403});if(path.includes('/assets/media-groups'))return Response.json({items:[],hasMore:false});return Response.json({items:[],hasMore:false});})as typeof fetch;
 render(<MediaUpload gallery/>);fireEvent.click(await screen.findByRole('button',{name:'سحب النشر'}));fireEvent.click(screen.getByRole('button',{name:'تأكيد سحب النشر'}));await screen.findByText('مسحوبة من الأسر والمدرب · الأصل للمراجعة الإدارية فقط');expect(screen.queryByRole('button',{name:'سحب النشر'})).not.toBeInTheDocument();expect(screen.getByRole('button',{name:'مراجعة نسخة جديدة'})).toBeInTheDocument();accessible=false;fireEvent.focus(window);await screen.findByText('تعذر تحميل المعرض.');expect(screen.queryByText('نسخة اصطناعية')).not.toBeInTheDocument();
});
it('review selects actual visible children; missing consent is disabled and no-identifiable-children clears selections',async()=>{
 global.fetch=vi.fn(async()=>Response.json(audience))as typeof fetch;render(<form><GroupMediaAudienceFields groupId="g"/></form>);const a=await screen.findByRole('checkbox',{name:/طفل موافق/});expect(screen.getByRole('checkbox',{name:/طفل بلا موافقة/})).toBeDisabled();fireEvent.click(a);expect(new FormData(a.closest('form')!).get('subjectPlayerIds')).toBe('p');fireEvent.click(screen.getByRole('checkbox',{name:'لا يوجد طفل يمكن التعرف عليه في اللقطة'}));expect(a).not.toBeChecked();expect(a).toBeDisabled();expect(new FormData(a.closest('form')!).get('subjectPlayerIds')).toBe('');expect(screen.getByLabelText('تاريخ التصوير')).toHaveAttribute('max','2026-10-18');
});
it('group upload sends the dated reviewed subject set to the real multipart endpoint contract',async()=>{
 let sent:FormData|undefined;global.fetch=vi.fn(async(p,o)=>{const path=String(p);if(path.endsWith('/csrf'))return Response.json({token:'t'});if(path.endsWith('/upload')){sent=o?.body as FormData;return Response.json({id:'a',url:'/api/v1/assets/a',contentType:'image/png'},{status:201});}if(path.includes('media-audience'))return Response.json(audience);if(path.includes('/assets/media-groups'))return Response.json({items:[{id:'g',arabicName:'مجموعة اختبار'}],hasMore:false});return Response.json({items:[],hasMore:false});})as typeof fetch;
 render(<MediaUpload/>);fireEvent.change(screen.getByRole('combobox',{name:'النشر إلى'}),{target:{value:'group'}});await waitFor(()=>expect(screen.getByRole('option',{name:'مجموعة اختبار'})).toBeInTheDocument());fireEvent.change(screen.getByRole('combobox',{name:/^المجموعة$/}),{target:{value:'g'}});fireEvent.click(await screen.findByRole('checkbox',{name:/طفل موافق/}));expect(screen.getByText('الصيغ وحدود الرفع')).toBeVisible();fireEvent.change(screen.getByLabelText('الصورة أو الفيديو'),{target:{files:[new File(['synthetic-pixel'],'pixel.png',{type:'image/png'})]}});fireEvent.click(screen.getByRole('checkbox',{name:/راجعت الملف/}));fireEvent.submit(screen.getByRole('button',{name:'رفع ونشر'}).closest('form')!);await waitFor(()=>expect(sent).toBeDefined());expect(sent!.get('groupId')).toBe('g');expect(sent!.get('subjectPlayerIds')).toBe('p');expect(sent!.get('eventOn')).toBe('2026-10-18');expect(sent!.get('privacyConfirmed')).toBe('true');expect(sent!.get('noRecognizableChildren')).toBe('false');
});

it('a changed file requires another review and an unknown write result opens the gallery check', async () => {
  let writes = 0;
  global.fetch = vi.fn(async (url) => {
    const path = String(url);
    if (path.endsWith('/csrf')) return Response.json({token:'t'});
    if (path.endsWith('/upload')) { writes++; return Response.json({}, {status:503}); }
    if (path.includes('/api/v1/assets/media-players')) return Response.json({items:[{id:'p',arabicName:'لاعب اختباري'}],hasMore:false});
    return Response.json({});
  }) as typeof fetch;
  render(<MediaUpload />);
  await screen.findByRole('option',{name:'لاعب اختباري'});
  fireEvent.change(screen.getByRole('combobox',{name:'اللاعب'}),{target:{value:'p'}});
  const picker = screen.getByLabelText('الصورة أو الفيديو');
  const review = screen.getByRole('checkbox',{name:/راجعت الملف/});
  fireEvent.change(picker,{target:{files:[new File(['test'],'one.png',{type:'image/png'})]}});
  fireEvent.click(review);
  fireEvent.change(picker,{target:{files:[new File(['test'],'two.png',{type:'image/png'})]}});
  expect(review).not.toBeChecked();
  const save = screen.getByRole('button',{name:'رفع ونشر'});
  expect(save).toBeDisabled();
  expect(writes).toBe(0);
  fireEvent.click(review);
  fireEvent.submit(save.closest('form')!);
  expect(await screen.findByRole('link',{name:'عرض المعرض'})).toHaveAttribute('href','/dashboard/players/media');
  expect(screen.queryByRole('button',{name:'رفع ونشر'})).not.toBeInTheDocument();
  expect(writes).toBe(1);
});

it('a failed audience read preserves the date control and allows a corrected date to load', async () => {
  const requests: string[] = [];
  global.fetch = vi.fn(async url => {
    requests.push(String(url));
    return String(url).includes('eventOn=2026-10-18') ? Response.json(audience) : Response.json({}, {status:503});
  }) as typeof fetch;
  render(<GroupMediaAudienceFields groupId="g" />);
  expect(await screen.findByText('تعذر تحميل جمهور المجموعة.')).toBeVisible();
  const date = screen.getByLabelText('تاريخ التصوير');
  fireEvent.change(date,{target:{value:'2026-10-18'}});
  expect(await screen.findByRole('checkbox',{name:/طفل موافق/})).toBeEnabled();
  expect(requests.at(-1)).toContain('eventOn=2026-10-18');
});
