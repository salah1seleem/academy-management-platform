import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, expect, it } from 'vitest';
import { RecordedAbsenceLinks, RecordedAbsenceReview } from './recorded-absence-evidence';
afterEach(cleanup);
it('distinguishes players from enrollments and explicitly excludes unknown/future attendance', () => {
  render(<RecordedAbsenceReview data={{ playerCount: 1, enrollmentCount: 2, sessionCount: 3, fromDate: '2026-10-10', toDate: '2026-10-12' }}/>);
  expect(screen.getByText(/1 لاعب فريد · 2 تسجيل/)).toBeVisible();
  expect(screen.getByText(/غير المسجلة والمستقبلية لا تُحسب/)).toBeVisible();
  expect(screen.getByText(/دون رسالة أو عقوبة تلقائية/)).toBeVisible();
  expect(screen.queryByRole('link')).not.toBeInTheDocument();
});
it('uses actual saved session IDs, deduplicates shared evidence and declares the three-link limit', () => {
  const records = ['a', 'a', 'b', 'c', 'd'].map(SessionId => ({ SessionId, SessionDate: '2026-10-10', StartTime: '17:00:00' }));
  render(<RecordedAbsenceLinks records={records}/>);
  expect(screen.getAllByRole('link')).toHaveLength(3);
  expect(screen.getAllByRole('link')[0]).toHaveAttribute('href', '/dashboard/attendance/sessions/a/players');
  expect(screen.getByText(/تعرض أول 3 كشوف/)).toBeVisible();
  expect(screen.getByText(/إغلاق المتابعة لا يغير حضور/)).toBeVisible();
});
