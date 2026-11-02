import {cleanup,render,screen,within} from '@testing-library/react';
import {afterEach,expect,it,vi} from 'vitest';
import {NotificationInbox} from './notification-center';
import {AdminPaymentDetail} from './subscriptions';
vi.mock('next/navigation',()=>({useSearchParams:()=>new URLSearchParams(),useRouter:()=>({push:vi.fn()})}));
afterEach(()=>{cleanup();vi.restoreAllMocks();});

it('opens each owner payment notice at its saved individual, family or enrollment source without collecting or marking it',async()=>{
 const items=[
  {id:'single-notice',message:'إثبات تجديد فردي محفوظ',targetKind:'PaymentQueue',targetId:'exact-single',paymentReviewType:'Renewal'},
  {id:'family-notice',message:'إثبات تجديد أسرة محفوظ',targetKind:'PaymentQueue',targetId:'exact-family',paymentReviewType:'FamilyRenewal'},
  {id:'enrollment-notice',message:'إثبات اشتراك جديد محفوظ',targetKind:'PaymentQueue',targetId:'exact-enrollment',paymentReviewType:'Enrollment'},
 ];
 global.fetch=vi.fn(async()=>new Response(JSON.stringify({unread:3,total:3,items:items.map(n=>({...n,isRead:false,createdAtUtc:'2026-10-22',reward:false}))}),{status:200}));
 render(<NotificationInbox/>);
 await screen.findByText('3 غير مقروءة');
 for(const [message,href] of [
  ['إثبات تجديد فردي محفوظ','/dashboard/subscriptions/payments/exact-single'],
  ['إثبات تجديد أسرة محفوظ','/dashboard/family-renewals/exact-family'],
  ['إثبات اشتراك جديد محفوظ','/dashboard/enrollment-requests/exact-enrollment'],
 ]){
  const article=screen.getByText(message).closest('article')!;
  expect(within(article).getByRole('link',{name:'عرض طلب الدفع ومراجعته'})).toHaveAttribute('href',href);
 }
 expect(vi.mocked(fetch).mock.calls).toEqual([['/api/v1/notifications?page=1',{cache:'no-store'}]]);
});

it('does not guess a source from message text, an unknown classification, a missing id, or a different target kind',async()=>{
 const items=[
  {id:'historical',message:'إثبات اشتراك جديد تاريخي',targetKind:'PaymentQueue',targetId:'unclassified'},
  {id:'foreign',message:'مصدر غير متاح',targetKind:'PaymentQueue',targetId:'foreign',paymentReviewType:null},
  {id:'future',message:'نوع غير مدعوم',targetKind:'PaymentQueue',targetId:'future',paymentReviewType:'SomethingElse'},
  {id:'missing',message:'معرف غير متاح',targetKind:'PaymentQueue',paymentReviewType:'Renewal'},
  {id:'product',message:'إثبات منتج',targetKind:'ProductReview',targetId:'exact-product',paymentReviewType:'Renewal'},
 ];
 global.fetch=vi.fn(async()=>new Response(JSON.stringify({unread:0,total:5,items:items.map(n=>({...n,isRead:true,createdAtUtc:'2026-10-22',reward:false}))}),{status:200}));
 render(<NotificationInbox/>);await screen.findByText('إثبات اشتراك جديد تاريخي');
 expect(screen.queryByRole('link',{name:'عرض طلب الدفع ومراجعته'})).not.toBeInTheDocument();
 expect(screen.getByRole('link',{name:'مراجعة طلب المنتج'})).toHaveAttribute('href','/dashboard/product-sales/exact-product');
 expect(vi.mocked(fetch).mock.calls).toHaveLength(1);
});

it('retains the confirmed renewal receipt and isolates the long player code direction without offering collection again',async()=>{
 const code='IMP-14204d109fcd42d28452';
 global.fetch=vi.fn(async path=>new Response(JSON.stringify(String(path)==='/api/v1/subscriptions/payments/exact-single'?{id:'exact-single',player:'لاعب اصطناعي',playerCode:code,sport:'كرة القدم',plan:'باقة اصطناعية',status:'Confirmed',renewalStatus:'Paid',originalAmount:1000,discountAmount:0,finalAmount:1000,currency:'EGP',createdAtUtc:'2026-10-22T10:00:00Z',isCurrent:true,canCollect:false,provider:'Manual',renewalId:'exact-renewal',receiptId:'exact-receipt'}:String(path).endsWith('/proof')?{url:'/api/v1/assets/synthetic-proof',status:'Accepted'}:{groups:[]}),{status:200}));
 render(<AdminPaymentDetail id="exact-single"/>);
 expect((await screen.findByText(code)).closest('bdi')).toHaveAttribute('dir','ltr');
 expect(screen.getByRole('link',{name:'عرض الإيصال'})).toHaveAttribute('href','/dashboard/subscriptions/receipts/exact-receipt');
 expect(screen.queryByRole('button',{name:'تأكيد تحصيل إلكتروني'})).not.toBeInTheDocument();
 expect(screen.queryByRole('button',{name:'قبول وتفعيل الاشتراك'})).not.toBeInTheDocument();
 expect(vi.mocked(fetch).mock.calls.every(([,init])=>!init?.method||init.method==='GET')).toBe(true);
});
