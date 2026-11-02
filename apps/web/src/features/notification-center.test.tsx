import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import {afterEach,expect,it,vi} from 'vitest';
import {NotificationInbox,NotificationTemplates,NotificationHistory} from './notification-center';
vi.mock('next/navigation',()=>({useSearchParams:()=>new URLSearchParams()}));
afterEach(()=>{cleanup();vi.restoreAllMocks();});
const response=(data:unknown,status=200)=>new Response(JSON.stringify(data),{status});
it('inbox persists unread state and links each committed event to its own resource',async()=>{
 let read=false;const writes:unknown[]=[];
 global.fetch=vi.fn(async(path,init)=>{
  if(String(path).endsWith('/csrf'))return response({token:'csrf'});
  if(init?.method==='PUT'){const body=JSON.parse(String(init.body));writes.push(body);read=body.isRead;return new Response(null,{status:204});}
  return response({unread:read?0:1,total:3,items:[{id:'notice',message:'تعديل أيام طفل اصطناعي',isRead:read,createdAtUtc:'2026-10-21',reward:false,targetKind:'SubscriptionPeriod',targetId:'exact-period'},
  {id:'family',message:'تجديد طفلة أ وطفل ب',isRead:true,createdAtUtc:'2026-10-21',reward:false,targetKind:'FamilyRenewal',targetId:'exact-family'}, {id:'product',message:'إثبات طلب منتج اصطناعي',isRead:true,createdAtUtc:'2026-10-21',reward:false,targetKind:'ProductReview',targetId:'exact-order'}]});
 });
 render(<NotificationInbox/>);
 expect(await screen.findByRole('link',{name:'عرض الاشتراك وتعديلاته'})).toHaveAttribute('href','/guardian/subscriptions/periods/exact-period');
 expect(screen.getByRole('link',{name:'عرض تجديد الأسرة'})).toHaveAttribute('href','/guardian/family-renewals/exact-family');
 expect(screen.getByRole('link',{name:'مراجعة طلب المنتج'})).toHaveAttribute('href','/dashboard/product-sales/exact-order');
 fireEvent.click(screen.getByRole('button',{name:'تمت القراءة'}));await waitFor(()=>expect(screen.getByText('0 غير مقروءة')).toBeInTheDocument());expect(writes).toEqual([{isRead:true}]);
});
it('template editor lists actual event variables and saving a template never sends a notification',async()=>{
 const calls:{path:string;method?:string;body?:unknown}[]=[];
 global.fetch=vi.fn(async(path,init)=>{
  calls.push({path:String(path),method:init?.method,body:init?.body?JSON.parse(String(init.body)):undefined});
  if(String(path).endsWith('/csrf'))return response({token:'csrf'});
  if(init?.method==='PUT')return new Response(null,{status:204});
  return response([{key:'RecordedAbsence',message:'غاب {اسم_اللاعب} عن حصة {التاريخ}',variables:['اسم_اللاعب','التاريخ']}]);
 });
 render(<NotificationTemplates/>);await screen.findByRole('group',{name:'غياب حصة مسجل'});expect(screen.getByText(/المتغيرات المتاحة:.*اسم_اللاعب/)).toBeInTheDocument();
 fireEvent.click(screen.getByRole('button',{name:'حفظ القالب'}));await waitFor(()=>expect(calls.some(c=>c.method==='PUT')).toBe(true));
 expect(calls.find(c=>c.method==='PUT')).toMatchObject({path:'/api/v1/manage/notifications/templates/RecordedAbsence',body:{message:'غاب {اسم_اللاعب} عن حصة {التاريخ}'}});
 expect(calls.some(c=>c.path.endsWith('/send'))).toBe(false);
});
it('template controls persist enablement and timing with the reviewed version and verify the saved row',async()=>{
 const initial={key:'Expiring',message:'ينتهي {children}',variables:['children'],enabled:true,version:11,timingDays:7,condition:'فترة مؤرخة فعلية',recipients:['ولي الأمر المرتبط']};let row=initial;const writes:unknown[]=[];
 global.fetch=vi.fn(async(path,init)=>{
  if(String(path).endsWith('/csrf'))return response({token:'csrf'});
  if(init?.method==='PUT'){const b=JSON.parse(String(init.body));writes.push(b);row={...row,message:b.message,enabled:b.enabled,timingDays:b.timingDays,version:12};return response({version:12});}
  return response([row]);
 });
 render(<NotificationTemplates/>);await screen.findByText('فترة مؤرخة فعلية');
 fireEvent.click(screen.getByRole('checkbox',{name:'تفعيل الحدث'}));fireEvent.change(screen.getByLabelText('التنبيه قبل الانتهاء بالأيام'),{target:{value:'3'}});fireEvent.click(screen.getByRole('button',{name:'حفظ القالب'}));
 await screen.findByRole('status');expect(writes).toEqual([{message:'ينتهي {children}',expectedVersion:11,enabled:false,timingDays:3}]);
 expect(screen.getByRole('checkbox',{name:'تفعيل الحدث'})).not.toBeChecked();expect(screen.getByLabelText('التنبيه قبل الانتهاء بالأيام')).toHaveValue(3);
});
it('a rejected stale save never reports success or silently overwrites the new server policy',async()=>{
 global.fetch=vi.fn(async(path,init)=>String(path).endsWith('/csrf')?response({token:'csrf'}):init?.method==='PUT'?response({message:'تغير إعداد التنبيه؛ أعد تحميله قبل الحفظ.'},409):response([{key:'DaysAdded',message:'أضيفت أيام',enabled:true,version:9,variables:[]} ]));
 render(<NotificationTemplates/>);await screen.findByRole('checkbox',{name:'تفعيل الحدث'});fireEvent.click(screen.getByRole('button',{name:'حفظ القالب'}));await screen.findByText('تغير إعداد التنبيه؛ أعد تحميله قبل الحفظ.');expect(screen.queryByRole('status')).not.toBeInTheDocument();
});

it('history distinguishes a suppressed event from an unread in-app event and uses server pages',async()=>{
 const paths:string[]=[];global.fetch=vi.fn(async(path)=>{paths.push(String(path));if(String(path).endsWith('/automation'))return response({scheduledEnabled:false,externalDelivery:false});return response({total:26,items:[{id:'suppressed',eventKey:'DaysAdded',message:'حدث معطل',recipient:'أسرة اختبار',audienceRole:'Guardian',status:'Disabled',isRead:false,createdAtUtc:'2026-10-21'},{id:'stored',eventKey:'RecordedAbsence',message:'غياب محفوظ',recipient:'أسرة ثانية',audienceRole:'Guardian',status:'StoredInApp',isRead:false,createdAtUtc:'2026-10-21'}]});});
 render(<NotificationHistory/>);await screen.findByText('الحدث معطل؛ لم يظهر في صندوق المستلم');expect(screen.getByText('محفوظ داخل التطبيق · لم يقرأه المستلم بعد')).toBeInTheDocument();
 expect(await screen.findByText(/غير مفعّل في هذه البيئة/)).toBeInTheDocument();fireEvent.click(screen.getByRole('button',{name:'التالي'}));await waitFor(()=>expect(paths.some(p=>p.includes('page=2'))).toBe(true));
});

it('describes a current stored event with no event key as unclassified without inventing a historical date or recipient',async()=>{
 global.fetch=vi.fn(async path=>String(path).endsWith('/automation')?response({scheduledEnabled:false,externalDelivery:false}):response({total:1,items:[{id:'recent-correction',eventKey:null,message:'قرار تصحيح حضور محفوظ اليوم',recipient:null,audienceRole:'AcademyAdmin',status:'StoredInApp',isRead:false,createdAtUtc:'2026-10-22T18:00:00Z'}]}));
 render(<NotificationHistory/>);
 await screen.findByText('تنبيه غير مصنّف');
 expect(screen.getByText('محفوظ داخل التطبيق · لم يقرأه المستلم بعد')).toBeInTheDocument();
 expect(screen.getByText(/حساب غير متاح/)).toBeInTheDocument();
 expect(screen.queryByText('حدث تاريخي غير مصنف')).not.toBeInTheDocument();
 expect(screen.queryByText(/حساب تاريخي/)).not.toBeInTheDocument();
});
it('reward notices open the persisted reward id while legacy notices retain the ledger link',async()=>{
 global.fetch=vi.fn(async()=>response({unread:0,total:2,items:[
  {id:'reward-notice',message:'مكافأة محفوظة محددة',isRead:true,createdAtUtc:'2026-10-23',reward:true,targetKind:'Rewards',targetId:'exact-reward'},
  {id:'old-reward-notice',message:'مكافأة تاريخية بلا معرف',isRead:true,createdAtUtc:'2026-10-23',reward:true,targetKind:'Rewards'},
 ]}));
 render(<NotificationInbox/>);await screen.findByText('مكافأة محفوظة محددة');
 expect(screen.getAllByRole('link',{name:'عرض المكافأة'}).map(a=>a.getAttribute('href'))).toEqual(['/guardian/rewards/exact-reward','/guardian/rewards']);
 expect(vi.mocked(fetch).mock.calls).toHaveLength(1);
});
