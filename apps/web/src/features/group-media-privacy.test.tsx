import{cleanup,fireEvent,render,screen,waitFor}from'@testing-library/react';
import{afterEach,expect,it,vi}from'vitest';
import{GroupMediaAudienceFields,GroupMediaConsent}from'./group-media-privacy';
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
it('review selects actual visible children; missing consent is disabled and no-identifiable-children clears selections',async()=>{
 global.fetch=vi.fn(async()=>Response.json(audience))as typeof fetch;render(<form><GroupMediaAudienceFields groupId="g"/></form>);const a=await screen.findByRole('checkbox',{name:/طفل موافق/});expect(screen.getByRole('checkbox',{name:/طفل بلا موافقة/})).toBeDisabled();fireEvent.click(a);expect(new FormData(a.closest('form')!).get('subjectPlayerIds')).toBe('p');fireEvent.click(screen.getByRole('checkbox',{name:'لا يوجد طفل يمكن التعرف عليه في هذه اللقطة'}));expect(a).not.toBeChecked();expect(a).toBeDisabled();expect(new FormData(a.closest('form')!).get('subjectPlayerIds')).toBe('');expect(screen.getByLabelText('تاريخ الواقعة')).toHaveAttribute('max','2026-10-18');
});
it('group upload sends the dated reviewed subject set to the real multipart endpoint contract',async()=>{
 let sent:FormData|undefined;global.fetch=vi.fn(async(p,o)=>{const path=String(p);if(path.endsWith('/csrf'))return Response.json({token:'t'});if(path.endsWith('/upload')){sent=o?.body as FormData;return Response.json({id:'a',url:'/api/v1/assets/a',contentType:'image/png'},{status:201});}if(path.includes('media-audience'))return Response.json(audience);if(path.includes('/structure/options'))return Response.json({groups:[{id:'g',arabicName:'مجموعة اختبار'}]});return Response.json([]);})as typeof fetch;
 render(<MediaUpload/>);fireEvent.change(screen.getByRole('combobox',{name:'رفع إلى'}),{target:{value:'group'}});await waitFor(()=>expect(screen.getByRole('option',{name:'مجموعة اختبار'})).toBeInTheDocument());fireEvent.change(screen.getByRole('combobox',{name:/^المجموعة$/}),{target:{value:'g'}});fireEvent.click(await screen.findByRole('checkbox',{name:/طفل موافق/}));fireEvent.change(screen.getByLabelText('الصورة أو الفيديو'),{target:{files:[new File(['synthetic-pixel'],'pixel.png',{type:'image/png'})]}});fireEvent.click(screen.getByRole('checkbox',{name:/راجعت الملف/}));fireEvent.submit(screen.getByRole('button',{name:'رفع ونشر بعد مراجعة الخصوصية'}).closest('form')!);await waitFor(()=>expect(sent).toBeDefined());expect(sent!.get('groupId')).toBe('g');expect(sent!.get('subjectPlayerIds')).toBe('p');expect(sent!.get('eventOn')).toBe('2026-10-18');expect(sent!.get('privacyConfirmed')).toBe('true');expect(sent!.get('noRecognizableChildren')).toBe('false');
});
