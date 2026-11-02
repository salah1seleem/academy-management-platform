import { cleanup, render, screen } from '@testing-library/react';
import { afterEach, expect, it } from 'vitest';
import { EnrollmentTransfers } from './enrollment-transfers';
afterEach(cleanup);
it("does not render a fake transfer for an unchanged enrollment", () => { const { container } = render(<EnrollmentTransfers />); expect(container).toBeEmptyDOMElement(); });
it("displays the scheduled group without offering a guardian a write action", () => { render(<EnrollmentTransfers scheduled transfers={[{ effectiveDate: "2026-11-15", group: "مجموعة المساء" }]} />); expect(screen.getByLabelText("نقل مجموعة مجدول")).toHaveTextContent("مجموعة المساء"); expect(screen.queryByRole("button")).not.toBeInTheDocument(); });
it('distinguishes a single replacement from the recurring group history', () => {
  render(<EnrollmentTransfers moves={[{ date: '2026-11-15', group: 'بديل مسائي' }]} />);
  expect(screen.getByLabelText('حصص منقولة')).toHaveTextContent('بديل مسائي');
  expect(screen.queryByLabelText('سجل نقل المجموعة')).not.toBeInTheDocument();
  expect(screen.queryByRole('button')).not.toBeInTheDocument();
});
it('renders both histories without suppressing either', () => {
  render(<EnrollmentTransfers scheduled transfers={[{ effectiveDate: '2026-12-01', group: 'مجموعة جديدة' }]} moves={[{ date: '2026-11-15', group: 'بديل مسائي' }]} />);
  expect(screen.getByLabelText('نقل مجموعة مجدول')).toHaveTextContent('مجموعة جديدة');
  expect(screen.getByLabelText('حصص منقولة')).toHaveTextContent('بديل مسائي');
});
