import { AcademyDateDefaults } from "../../../../components/academy-date-defaults";
import { OperatingStatementReport } from "../../../../features/operating-statement";

export default function Page() {
  return <AcademyDateDefaults title="قائمة الأرباح والخسائر" context="الحسابات والماليات" requireBothDates><OperatingStatementReport /></AcademyDateDefaults>;
}
