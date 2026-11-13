import {cleanup,fireEvent,render,screen,waitFor,within} from '@testing-library/react';
import {afterEach,expect,it,vi} from 'vitest';
import {CollectionReminders,NotificationInbox,NotificationTemplates,NotificationHistory} from './notification-center';
let query=new URLSearchParams();
vi.mock('next/navigation',()=>({useSearchParams:()=>query}));
afterEach(()=>{cleanup();vi.restoreAllMocks();query=new URLSearchParams();});
const response=(data:unknown,status=200)=>new Response(JSON.stringify(data),{status});
it.each([true,false])("offers the family balance from a targetless reminder only in the guardian inbox (%s)",async guardian=>{
 global.fetch=vi.fn(async()=>response({unread:1,total:1,items:[{id:'receivable',message:'تذكير مستحقات اصطناعي',createdAtUtc:'2026-11-13',isRead:false,targetKind:'Receivables',targetId:null}]}));
 render(<NotificationInbox guardian={guardian}/>);
 await screen.findByText('تذكير مستحقات اصطناعي');
 const destination=screen.queryByRole('link',{name:'كشف المستحقات'});
 if(guardian)expect(destination).toHaveAttribute('href','/guardian/receivables');
 else expect(destination).not.toBeInTheDocument();
});
const reminderAudience={total:1,shown:1,template:'رسالة اصطناعية',items:[{id:'parent',name:'أسرة اصطناعية',children:[{name:'لاعب اصطناعي'}],amount:100,currency:'EGP'}]};

it('recovers the recipient read and removes the composer when the selected branch is empty',async()=>{
 query=new URLSearchParams({branchId:'a'});
 global.fetch=vi.fn().mockResolvedValueOnce(response({},503)).mockResolvedValueOnce(response({...reminderAudience,items:[],total:0,shown:0}));
 render(<CollectionReminders/>);
 expect(await screen.findByRole('alert')).toHaveTextContent('تعذر تحميل قائمة المستلمين');
 expect(screen.queryByRole('status')).not.toBeInTheDocument();
 fireEvent.click(screen.getByRole('button',{name:'تحديث قائمة المستلمين'}));
 expect(await screen.findByRole('link',{name:'مراجعة كشف المستحقات'})).toHaveAttribute('href','/dashboard/finance/receivables?state=overdue&branchId=a');
 expect(screen.queryByRole('checkbox')).not.toBeInTheDocument();
 expect(screen.queryByRole('textbox')).not.toBeInTheDocument();
});

it('clears the old audience and message on navigation and shows a distinct denied state',async()=>{
 query=new URLSearchParams({branchId:'a'});
 global.fetch=vi.fn(async path=>String(path).includes('branchId=a')?response(reminderAudience):response({},403));
 const view=render(<CollectionReminders/>);
 fireEvent.click(await screen.findByRole('checkbox',{name:/أسرة اصطناعية/}));
 expect(screen.getByRole('button',{name:'معاينة التنبيه'})).toBeVisible();
 query=new URLSearchParams({branchId:'b'});
 view.rerender(<CollectionReminders/>);
 expect(screen.queryByText('أسرة اصطناعية')).not.toBeInTheDocument();
 expect(await screen.findByRole('alert')).toHaveTextContent('غير متاحة لهذا الحساب');
 expect(screen.queryByRole('button',{name:'تحديث قائمة المستلمين'})).not.toBeInTheDocument();
});

it('keeps the exact attempted send after a lost reply and reloads after a stale preview',async()=>{
 const posts:unknown[]=[];
 let count=0;
 global.fetch=vi.fn(async (path,init)=>{
  const url=String(path);
  if(url.endsWith('/csrf'))return response({token:'csrf'});
  if(url.endsWith('/preview'))return response({previewHash:'confirmed',count:1,messages:[{id:'parent',message:'نص المعاينة'}]});
  if(url.endsWith('/send')){
   posts.push(JSON.parse(String(init?.body)));
   if(++count===1)throw new TypeError('network');
   return response({message:'تغيّرت البيانات'},409);
  }
  return response(reminderAudience);
 });
 render(<CollectionReminders/>);
 fireEvent.click(await screen.findByRole('checkbox',{name:/أسرة اصطناعية/}));
 fireEvent.click(screen.getByRole('button',{name:'معاينة التنبيه'}));
 expect(await screen.findByText('نص المعاينة')).toBeVisible();
 expect(posts).toHaveLength(0);
 fireEvent.click(screen.getByRole('button',{name:/تأكيد إرسال/}));
 const retry=await screen.findByRole('button',{name:'إعادة تأكيد الإرسال'});
 expect(screen.getByRole('textbox',{name:'نص التنبيه'})).toBeDisabled();
 fireEvent.click(retry);
 expect(await screen.findByRole('button',{name:'تحديث قائمة المستلمين'})).toBeVisible();
 expect(screen.queryByText('نص المعاينة')).not.toBeInTheDocument();
 expect(posts).toHaveLength(2);
 expect(posts[0]).toEqual(posts[1]);
});
it('inbox persists unread state and links each committed event to its own resource',async()=>{
 let read=false;const writes:unknown[]=[];
 global.fetch=vi.fn(async(path,init)=>{
  if(String(path).endsWith('/csrf'))return response({token:'csrf'});
  if(init?.method==='PUT'){const body=JSON.parse(String(init.body));writes.push(body);read=body.isRead;return new Response(null,{status:204});}
  return response({unread:read?0:1,total:3,items:[{id:'notice',message:'تعديل أيام طفل اصطناعي',isRead:read,createdAtUtc:'2026-10-21',reward:false,targetKind:'SubscriptionPeriod',targetId:'exact-period'},
  {id:'family',message:'تجديد طفلة أ وطفل ب',isRead:true,createdAtUtc:'2026-10-21',reward:false,targetKind:'FamilyRenewal',targetId:'exact-family'}, {id:'product',message:'إثبات طلب منتج اصطناعي',isRead:true,createdAtUtc:'2026-10-21',reward:false,targetKind:'ProductReview',targetId:'exact-order'}]});
 });
 render(<NotificationInbox guardian/>);
 expect(await screen.findByRole('link',{name:'عرض الاشتراك وتعديلاته'})).toHaveAttribute('href','/guardian/subscriptions/periods/exact-period');
 expect(screen.getByRole('link',{name:'عرض تجديد الأسرة'})).toHaveAttribute('href','/guardian/family-renewals/exact-family');
 expect(screen.queryByRole('link',{name:'مراجعة طلب المنتج'})).not.toBeInTheDocument();
 const article = screen.getByText('تعديل أيام طفل اصطناعي').closest('article')!;
 const button = within(article).getByRole('button',{name:'تحديد كمقروء'});
 button.focus(); fireEvent.click(button); fireEvent.click(button);
 await screen.findByText('غير المقروءة 0');
 expect(within(article).getByRole('button',{name:'تحديد كغير مقروء'})).toHaveFocus();
 expect(writes).toEqual([{isRead:true}]);
});
it('recovers a failed inbox read without presenting loading and error together', async () => {
 global.fetch = vi.fn().mockRejectedValueOnce(new TypeError('Failed to fetch')).mockResolvedValueOnce(response({items: [], unread: 0, total: 0}));
 render(<NotificationInbox guardian/>);
 expect(await screen.findByRole('alert')).toHaveTextContent('تعذر تحميل التنبيهات.');
 expect(screen.queryByText('جارٍ تحميل التنبيهات…')).not.toBeInTheDocument();
 fireEvent.click(screen.getByRole('button', {name: 'إعادة المحاولة'}));
 expect(await screen.findByText('لا توجد تنبيهات.')).toBeVisible();
 expect(screen.queryByRole('navigation',{name:'صفحات التنبيهات'})).not.toBeInTheDocument();
});
it('checks the saved status on the same page after a lost write reply', async () => {
 let saved = false;
 const writes: unknown[] = [];
 global.fetch = vi.fn(async (path, init) => {
  if (String(path).endsWith('/csrf')) return response({token:'csrf'});
  if (init?.method === 'PUT') {
   writes.push(JSON.parse(String(init.body)));
   saved = true;
   throw new TypeError('Connection closed');
  }
  const second = String(path).endsWith('page=2');
  return response({unread: saved ? 0 : 1, total: 26, items: [{id: second ? 'second' : 'first', message: second ? 'تنبيه في الصفحة الثانية' : 'تنبيه في الصفحة الأولى', createdAtUtc: '2026-10-21', isRead: saved || !second, reward: false}]});
 });
 render(<NotificationInbox/>);
 fireEvent.click(await screen.findByRole('button',{name:'التالي'}));
 await screen.findByText('تنبيه في الصفحة الثانية');
 fireEvent.click(screen.getByRole('button',{name:'تحديد كمقروء'}));
 expect(await screen.findByRole('alert')).toHaveTextContent('تعذر تأكيد تحديث الحالة.');
 expect(screen.getByText('غير المقروءة 1')).toBeVisible();
 fireEvent.click(screen.getByRole('button',{name:'تحديث القائمة'}));
 expect(await screen.findByRole('button',{name:'تحديد كغير مقروء'})).toBeVisible();
 expect(screen.getByText('صفحة 2 من 2')).toBeVisible();
 expect(writes).toEqual([{isRead:true}]);
});
it('links directly to the stored match for staff and exposes no guardian payment destination', async () => {
 global.fetch = vi.fn(async () => response({unread: 2, total: 2, items: [
  {id: 'match', message: 'موعد مباراة اصطناعية', isRead: false, createdAtUtc: '2026-10-21', matchId: 'match-target', targetKind: 'Matches', reward: false},
  {id: 'payment', message: 'دفع أسرة اصطناعية', isRead: false, createdAtUtc: '2026-10-21', targetKind: 'Payment', targetId: 'payment-target', reward: false},
 ]}));
 render(<NotificationInbox/>);
 expect(await screen.findByRole('link',{name:'عرض المباراة'})).toHaveAttribute('href','/dashboard/matches?matchId=match-target');
 expect(screen.queryByRole('link',{name:'عرض طلب الدفع'})).not.toBeInTheDocument();
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
 render(<NotificationInbox guardian/>);await screen.findByText('مكافأة محفوظة محددة');
 expect(screen.getAllByRole('link',{name:'عرض المكافأة'}).map(a=>a.getAttribute('href'))).toEqual(['/guardian/rewards/exact-reward','/guardian/rewards']);
 expect(vi.mocked(fetch).mock.calls).toHaveLength(1);
});
