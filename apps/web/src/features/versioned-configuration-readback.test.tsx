import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import {afterEach,expect,it,vi} from 'vitest';
import {VersionedConfiguration} from './versioned-configuration';
afterEach(()=>{cleanup();vi.restoreAllMocks();});
const settings={currency:'EGP',timeZone:'Africa/Cairo',features:{nutrition:true},cashEnabled:true,consecutiveAbsenceAlert:3,tenantMarketingOptIn:false};
const active={id:'v1',revision:1,version:5,state:'Active',contentHash:'a'.repeat(64),settings};
const json=(value:unknown,status=200)=>new Response(JSON.stringify(value),{status,headers:{'Content-Type':'application/json'}});
function server(options:{failRead?:boolean;mismatch?:boolean;transition?:boolean}={}){
 let writes=0,reads=0,persisted:typeof active|null=null,failRead=options.failRead??false;
 global.fetch=vi.fn(async(input,init)=>{
  const path=String(input);
  if(path.endsWith('/my-permissions'))return json(['academy.settings.manage']);
  if(path.endsWith('/me'))return json({role:'AcademyOwner'});
  if(path.endsWith('/help-options'))return json({lessons:[]});
  if(path.endsWith('/csrf'))return json({token:'test'});
  if(init?.method==='POST'){
   writes++;
   if(path.endsWith('/transition')){persisted={...active,state:'Suspended',version:6};return new Response(null,{status:204});}
   persisted={...active,id:'v2',revision:2,version:1,state:'Draft',settings:JSON.parse(String(init.body))};
   return json({id:'v2',revision:2});
  }
  reads++;if(writes&&failRead)return json({},503);
  const row=persisted&&options.mismatch?{...persisted,settings}:{...persisted};
  return json({academyCurrency:'EGP',entitlements:[{feature:'nutrition',enabled:true,planCode:'Core'}],versions:options.transition&&persisted?[persisted]:persisted?[row,active]:[active],history:[]});
 });
 return {writes:()=>writes,reads:()=>reads,recover:()=>{failRead=false;}};
}
it('announces a draft only after the returned id and submitted settings are read back',async()=>{
 const api=server(),notice=vi.fn();window.addEventListener('academy-save-success',notice);
 try{render(<VersionedConfiguration/>);fireEvent.change(await screen.findByLabelText('تنبيه الغياب المتتالي'),{target:{value:'4'}});fireEvent.click(screen.getByRole('button',{name:'حفظ إصدار مسودة'}));
 await waitFor(()=>expect(notice).toHaveBeenCalledTimes(1));expect(api.writes()).toBe(1);expect(api.reads()).toBe(2);expect(screen.getByText('الإصدار 2 · مسودة')).toBeVisible();expect(screen.getByLabelText('تنبيه الغياب المتتالي')).toHaveValue(4);
 }finally{window.removeEventListener('academy-save-success',notice);}
});
it('keeps an accepted draft pending after a failed GET and retries only the read',async()=>{
 const api=server({failRead:true}),notice=vi.fn();window.addEventListener('academy-save-success',notice);
 try{render(<VersionedConfiguration/>);fireEvent.change(await screen.findByLabelText('تنبيه الغياب المتتالي'),{target:{value:'4'}});fireEvent.click(screen.getByRole('button',{name:'حفظ إصدار مسودة'}));
 const retry=await screen.findByRole('button',{name:'إعادة التحقق من الحفظ'});expect(notice).not.toHaveBeenCalled();expect(screen.getByRole('button',{name:'حفظ إصدار مسودة'})).toBeDisabled();expect(screen.getByLabelText('تنبيه الغياب المتتالي')).toHaveValue(4);
 api.recover();fireEvent.click(retry);await waitFor(()=>expect(notice).toHaveBeenCalledTimes(1));expect(api.writes()).toBe(1);expect(screen.queryByRole('button',{name:'إعادة التحقق من الحفظ'})).not.toBeInTheDocument();
 }finally{window.removeEventListener('academy-save-success',notice);}
});
it('does not accept the right draft id with different persisted settings',async()=>{
 const api=server({mismatch:true}),notice=vi.fn();window.addEventListener('academy-save-success',notice);
 try{render(<VersionedConfiguration/>);fireEvent.change(await screen.findByLabelText('تنبيه الغياب المتتالي'),{target:{value:'4'}});fireEvent.click(screen.getByRole('button',{name:'حفظ إصدار مسودة'}));
 fireEvent.click(await screen.findByRole('button',{name:'إعادة التحقق من الحفظ'}));await waitFor(()=>expect(api.reads()).toBe(3));expect(notice).not.toHaveBeenCalled();expect(api.writes()).toBe(1);expect(screen.getByLabelText('تنبيه الغياب المتتالي')).toHaveValue(4);
 }finally{window.removeEventListener('academy-save-success',notice);}
});
it('verifies a transition by the same version id and target state without replaying it',async()=>{
 const api=server({failRead:true,transition:true}),notice=vi.fn();window.addEventListener('academy-save-success',notice);
 try{render(<VersionedConfiguration/>);fireEvent.click(await screen.findByRole('button',{name:'إيقاف الإصدار'}));fireEvent.click(screen.getByRole('button',{name:'تأكيد الاعتماد'}));
 const retry=await screen.findByRole('button',{name:'إعادة التحقق من الحفظ'});expect(notice).not.toHaveBeenCalled();api.recover();fireEvent.click(retry);await waitFor(()=>expect(notice).toHaveBeenCalledTimes(1));expect(api.writes()).toBe(1);expect(screen.getByText('الإصدار 1 · موقوفة')).toBeVisible();
 }finally{window.removeEventListener('academy-save-success',notice);}
});
it('keeps a transport-uncertain save locked, hides technical text and never retries POST',async()=>{
 const api=server(),fetcher=global.fetch;
 global.fetch=vi.fn(async(input,init)=>{if(init?.method==='POST')throw new TypeError('Network request failed');return fetcher(input,init);});
 render(<VersionedConfiguration/>);fireEvent.click(await screen.findByRole('button',{name:'حفظ إصدار مسودة'}));
 const retry=await screen.findByRole('button',{name:'إعادة التحقق من الحفظ'});await waitFor(()=>expect(retry).toBeEnabled());
 expect(screen.queryByText('Network request failed')).not.toBeInTheDocument();expect(screen.getByRole('button',{name:'حفظ إصدار مسودة'})).toBeDisabled();
 fireEvent.click(retry);await waitFor(()=>expect(api.reads()).toBe(2));expect(vi.mocked(global.fetch).mock.calls.filter(([,init])=>init?.method==='POST')).toHaveLength(1);
});
it('a definite server rejection permits correction without a success notification',async()=>{
 server();const fetcher=global.fetch,notice=vi.fn();
 global.fetch=vi.fn(async(input,init)=>init?.method==='POST'?json({message:'تغير الإصدار؛ أعد تحميل الإعدادات.'},409):fetcher(input,init));
 window.addEventListener('academy-save-success',notice);
 try{render(<VersionedConfiguration/>);fireEvent.click(await screen.findByRole('button',{name:'حفظ إصدار مسودة'}));await screen.findByText('تغير الإصدار؛ أعد تحميل الإعدادات.');expect(screen.getByRole('button',{name:'حفظ إصدار مسودة'})).toBeEnabled();expect(notice).not.toHaveBeenCalled();expect(screen.queryByRole('button',{name:'إعادة التحقق من الحفظ'})).not.toBeInTheDocument();}finally{window.removeEventListener('academy-save-success',notice);}
});

it('explicit reload reviews current versions and abandons an uncertain local draft without any POST',async()=>{
 const api=server({mismatch:true});render(<VersionedConfiguration/>);
 fireEvent.change(await screen.findByLabelText('تنبيه الغياب المتتالي'),{target:{value:'4'}});fireEvent.click(screen.getByRole('button',{name:'حفظ إصدار مسودة'}));
 const reload=await screen.findByRole('button',{name:'إعادة تحميل الإصدارات الحالية'});await waitFor(()=>expect(reload).toBeEnabled());fireEvent.click(reload);
 await screen.findByText('أُعيدت قراءة الإصدارات الحالية. راجعها قبل إرسال تغيير جديد.');expect(api.writes()).toBe(1);expect(screen.getByLabelText('تنبيه الغياب المتتالي')).toHaveValue(3);expect(screen.queryByRole('button',{name:'إعادة التحقق من الحفظ'})).not.toBeInTheDocument();
});

it('a server error after POST remains uncertain and cannot be resubmitted automatically',async()=>{
 server();const fetcher=global.fetch;global.fetch=vi.fn(async(input,init)=>init?.method==='POST'?json({message:'تعذر إكمال الطلب.'},500):fetcher(input,init));
 render(<VersionedConfiguration/>);fireEvent.click(await screen.findByRole('button',{name:'حفظ إصدار مسودة'}));await screen.findByText('تعذر إكمال الطلب.');expect(screen.getByRole('button',{name:'حفظ إصدار مسودة'})).toBeDisabled();expect(screen.getByRole('button',{name:'إعادة التحقق من الحفظ'})).toBeEnabled();expect(vi.mocked(global.fetch).mock.calls.filter(([,init])=>init?.method==='POST')).toHaveLength(1);
});
