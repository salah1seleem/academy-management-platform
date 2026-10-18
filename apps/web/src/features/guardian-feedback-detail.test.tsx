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
