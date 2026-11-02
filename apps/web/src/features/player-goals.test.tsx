import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, expect, it, vi } from 'vitest';
import { GuardianPlayerGoals, PlayerGoalEditor, PlayerGoals } from './player-goals';
const { push } = vi.hoisted(() => ({ push: vi.fn() }));
vi.mock('next/navigation', () => ({ useRouter: () => ({ push }) }));
afterEach(() => { cleanup(); vi.restoreAllMocks(); push.mockClear(); });
const enrollment = { id: 'enrollment', playerId: 'child', player: 'لاعب اصطناعي', sport: 'كرة القدم', level: null };
const goal = { id: 'goal', sportEnrollmentId: 'enrollment', title: 'تمرير دقيق اصطناعي', target: 'نراجع الخطوة الفنية القادمة', reviewOn: '2026-10-31', state: 'Published', version: 7, revision: 2 };
it('opens the exact sport enrollment and preserves the player development context', async () => {
  global.fetch = vi.fn(async path => { expect(String(path)).toBe('/api/v1/evaluations/development-goals/enrollments/enrollment'); return Response.json({ enrollment, asOfDate: '2026-10-18', canCreate: true, items: [goal] }); }) as typeof fetch;
  render(<PlayerGoals enrollmentId="enrollment" />);
  expect(await screen.findByText(goal.title)).toBeVisible(); expect(screen.getByRole('link', { name: 'هدف جديد' })).toHaveAttribute('href', '/dashboard/evaluations/goals/new?enrollmentId=enrollment'); expect(screen.getByRole('link', { name: 'فتح الهدف وسجل المراجعة' })).toHaveAttribute('href', '/dashboard/evaluations/goals/goal');
});
it('retries a lost draft response with the same command ID and navigates only after success', async () => {
  const commands: Record<string, unknown>[] = [];
  global.fetch = vi.fn(async (path, init) => {
    if (String(path).endsWith('/csrf')) return Response.json({ token: 'csrf' });
    if (init?.method === 'POST') { commands.push(JSON.parse(init.body as string)); return commands.length === 1 ? Response.json({ message: 'تعذر تأكيد الحفظ' }, { status: 503 }) : Response.json({ id: 'new-goal' }); }
    return Response.json({ enrollment, asOfDate: '2026-10-18', canCreate: true, items: [] });
  }) as typeof fetch;
  render(<PlayerGoalEditor enrollmentId="enrollment" />); await screen.findByLabelText('عنوان الهدف');
  fireEvent.change(screen.getByLabelText('عنوان الهدف'), { target: { value: 'هدف فني اصطناعي' } }); fireEvent.change(screen.getByLabelText('ما الذي نعمل عليه؟'), { target: { value: 'خطوة واضحة' } });
  fireEvent.click(screen.getByRole('button', { name: 'حفظ المسودة' })); await screen.findByText('تعذر تأكيد الحفظ'); expect(push).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole('button', { name: 'حفظ المسودة' })); await waitFor(() => expect(push).toHaveBeenCalledWith('/dashboard/evaluations/goals/new-goal')); expect(commands).toHaveLength(2); expect(commands[0]).toEqual(commands[1]); expect(commands[0].requestId).toMatch(/^[a-f0-9-]{36}$/);
});
it('published targets are not editable; a review saves the real version without a promotion or financial action', async () => {
  let reviewed = false;
  global.fetch = vi.fn(async (path, init) => {
    if (String(path).endsWith('/csrf')) return Response.json({ token: 'csrf' });
    if (init?.method === 'POST') { expect(String(path)).toBe('/api/v1/evaluations/development-goals/goal/transition'); expect(JSON.parse(init.body as string)).toMatchObject({ action: 'Reviewed', note: 'تحسن فني اصطناعي', version: 7 }); reviewed = true; return Response.json({ id: 'goal' }); }
    return Response.json({ enrollment, asOfDate: '2026-10-18', item: { ...goal, version: reviewed ? 8 : 7 }, canEdit: true, history: [] });
  }) as typeof fetch;
  render(<PlayerGoalEditor id="goal" />); await screen.findByText(goal.title); expect(screen.queryByLabelText('عنوان الهدف')).not.toBeInTheDocument();
  fireEvent.change(screen.getByLabelText('ملاحظة التقدم للأسرة (اختياري)'), { target: { value: 'تحسن فني اصطناعي' } }); fireEvent.click(screen.getByRole('button', { name: 'حفظ المراجعة' })); expect(await screen.findByText('تم حفظ تحديث الهدف وسجل المراجعة.')).toBeVisible(); expect(reviewed).toBe(true);
});
it('read-only and revoked views do not retain a child goal or editing controls', async () => {
  let allowed = true; global.fetch = vi.fn(async () => allowed ? Response.json({ enrollment, item: goal, canEdit: false, history: [] }) : Response.json({}, { status: 404 })) as typeof fetch;
  render(<PlayerGoalEditor id="goal" />); await screen.findByText(goal.title); expect(screen.queryByRole('button', { name: 'حفظ المراجعة' })).not.toBeInTheDocument();
  allowed = false; fireEvent.focus(window); await waitFor(() => expect(screen.queryByText(goal.title)).not.toBeInTheDocument()); expect(await screen.findByText(/الهدف أو اللاعب غير متاح/)).toBeVisible();
});
it('Guardian sees only the linked published goal and public review, never training edit controls', async () => {
  global.fetch = vi.fn(async path => String(path).includes('/help') ? Response.json({items:[]}) : Response.json({hasMore:false,items:[{goal,sport:'كرة القدم',reviews:[{revision:3,action:'Reviewed',note:'مراجعة منشورة للأسرة',actorName:'مدرب اصطناعي',createdAtUtc:'2026-10-18T06:00:00Z'}]},{goal:{...goal,id:'draft',state:'Draft',title:'مسودة غير منشورة'},sport:'كرة القدم',reviews:[]}]})) as typeof fetch;
  render(<GuardianPlayerGoals playerId="child"/>); expect(await screen.findByText(goal.title)).toBeVisible();expect(screen.getByText('مراجعة منشورة للأسرة')).toBeVisible();expect(screen.queryByText('مسودة غير منشورة')).not.toBeInTheDocument();expect(screen.queryByRole('button',{name:'حفظ المراجعة'})).not.toBeInTheDocument();expect(screen.getByRole('link',{name:'العودة لملف اللاعب'})).toHaveAttribute('href','/guardian/children/child');
});
