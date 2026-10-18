import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { GuardianFeedbackDetail, type FeedbackDetail } from './guardian-feedback-detail';
import { OverdueFeedbackLinks, OverdueFeedbackReview } from './overdue-feedback-evidence';
afterEach(()=>{cleanup();vi.restoreAllMocks();});
const item:FeedbackDetail={id:'f1',kind:'Complaint',score:null,message:'شكوى اصطناعية خاصة',status:'Open',dueOn:'2026-10-10',overdue:true,ownerResponse:null,createdAtUtc:'2026-10-08T06:00:00Z',reviewedAtUtc:null,respondedAtUtc:null,playerId:'child',playerName:'طفل اصطناعي',trainingGroupId:'group',groupName:'مجموعة اصطناعية',branchName:'فرع اصطناعي',coachName:null};
it('uses the exact source ID and saves a real reply only from its own workspace',async()=>{
  let resolved=false;
  const fetcher=vi.fn(async(path:RequestInfo|URL,init?:RequestInit)=>{
    if(String(path).endsWith('/csrf'))return Response.json({token:'csrf'});
    if(init?.method==='POST'){expect(String(path)).toBe('/api/v1/manage/feedback/f1/resolve');expect(JSON.parse(init.body as string)).toEqual({response:'رد اصطناعي محفوظ'});resolved=true;return Response.json({id:'f1'});}
    return Response.json({...item,...(resolved?{status:'Resolved',ownerResponse:'رد اصطناعي محفوظ'}:{})});
  });global.fetch=fetcher as typeof fetch;
  render(<GuardianFeedbackDetail id="f1"/>);
  expect(await screen.findByText(item.message)).toBeVisible();expect(screen.getByRole('link',{name:'ملاحظات المجموعة نفسها'})).toHaveAttribute('href','/dashboard/feedback?groupId=group');
  fireEvent.change(screen.getByLabelText('الرد على الملاحظة'),{target:{value:'رد اصطناعي محفوظ'}});fireEvent.click(screen.getByRole('button',{name:'إرسال الرد وحل الملاحظة'}));
  expect(await screen.findByText('رد الإدارة: رد اصطناعي محفوظ')).toBeVisible();expect(screen.queryByRole('button',{name:'إرسال الرد وحل الملاحظة'})).not.toBeInTheDocument();
});
it('hides family text if live reauthorization fails on focus',async()=>{
  let revoked=false;global.fetch=vi.fn(async()=>revoked?Response.json({},{status:403}):Response.json(item)) as typeof fetch;
  render(<GuardianFeedbackDetail id="f1"/>);expect(await screen.findByText(item.message)).toBeVisible();revoked=true;fireEvent.focus(window);
  await waitFor(()=>expect(screen.queryByText(item.message)).not.toBeInTheDocument());expect(await screen.findByText(/ليست لديك صلاحية/)).toBeVisible();
});
it('separates notes and submitters, opens saved IDs and preserves group context',()=>{
  render(<><OverdueFeedbackReview data={{complaintCount:1,suggestionCount:1,submitterCount:1,oldestDueOn:'2026-10-10',asOfDate:'2026-10-18'}}/><OverdueFeedbackLinks ids={['a','b']} groupId="g"/></>);
  expect(screen.getByText('1 شكوى · 1 اقتراح · 1 حساب مقدم فريد')).toBeVisible();expect(screen.getByRole('link',{name:'فتح ملاحظة من دليل الإنشاء 1'})).toHaveAttribute('href','/dashboard/feedback/a');expect(screen.getByRole('link',{name:'كل الملاحظات المتأخرة للمجموعة نفسها'})).toHaveAttribute('href','/dashboard/feedback?groupId=g&status=Unresolved&overdue=true');
});
it('keeps a failed private command draft and reuses its request ID, then shows the saved stage and private journal',async()=>{
  let attempts=0, saved=false; const bodies:Record<string,unknown>[]=[];
  global.fetch=vi.fn(async(path:RequestInfo|URL,init?:RequestInit)=>{
    if(String(path).endsWith('/csrf'))return Response.json({token:'csrf'});
    if(init?.method==='POST') { expect(String(path)).toBe('/api/v1/manage/feedback/f1/transition'); bodies.push(JSON.parse(init.body as string)); attempts++; if(attempts===1)return Response.json({message:'تعذر حفظ المعالجة الآن.'},{status:503}); saved=true; return Response.json({id:'f1'}); }
    return Response.json({...item,version:saved?8:7,status:saved?'InProgress':'Open',history:saved?[{id:'c1',action:'Transition',fromStatus:'Open',toStatus:'InProgress',internalNote:'متابعة داخلية فقط',actorName:'مالك اصطناعي',createdAtUtc:'2026-10-19T07:00:00Z'}]:[]});
  }) as typeof fetch;
  render(<GuardianFeedbackDetail id="f1"/>);await screen.findByText(item.message);
  fireEvent.change(screen.getByLabelText('الخطوة التالية'),{target:{value:'InProgress'}});fireEvent.change(screen.getByLabelText('ملاحظة داخلية (اختياري)'),{target:{value:'متابعة داخلية فقط'}});
  fireEvent.click(screen.getByRole('button',{name:'حفظ مرحلة المعالجة'}));expect(await screen.findByText('تعذر حفظ المعالجة الآن.')).toBeVisible();
  expect(screen.getByLabelText('ملاحظة داخلية (اختياري)')).toHaveValue('متابعة داخلية فقط');
  fireEvent.click(screen.getByRole('button',{name:'حفظ مرحلة المعالجة'}));expect(await screen.findByText('ملاحظة داخلية: متابعة داخلية فقط')).toBeVisible();
  expect(bodies).toHaveLength(2);expect(bodies[1]).toEqual(bodies[0]);expect(bodies[0]).toMatchObject({status:'InProgress',version:7,internalNote:'متابعة داخلية فقط',requestId:expect.any(String)});
  expect(screen.getByRole('option',{name:'بانتظار معلومات'})).toBeInTheDocument();expect(screen.queryByRole('option',{name:'مغلقة'})).not.toBeInTheDocument();
});
it('closed cases allow explicit reopen only and show a bounded, escaped private history',async()=>{
  global.fetch=vi.fn(async()=>Response.json({...item,version:4,status:'Closed',ownerResponse:'رد سابق محفوظ',historyHasMore:true,history:[{id:'c1',action:'Transition',fromStatus:'Resolved',toStatus:'Closed',internalNote:'<img src=x onerror=alert(1)>',createdAtUtc:'2026-10-19T07:00:00Z'}]})) as typeof fetch;
  render(<GuardianFeedbackDetail id="f1"/>);await screen.findByText(item.message);
  expect(screen.getByRole('option',{name:'أعيد فتحها'})).toBeInTheDocument();expect(screen.queryByRole('button',{name:'إرسال الرد وحل الملاحظة'})).not.toBeInTheDocument();
  expect(screen.getByText('توجد تحديثات أقدم غير معروضة؛ لم تُحذف.')).toBeVisible();expect(screen.queryByRole('img')).not.toBeInTheDocument();
});
