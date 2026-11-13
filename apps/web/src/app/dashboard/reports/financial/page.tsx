import { FinancialReport } from "../../../../features/reports";
import { AcademyDateDefaults } from "../../../../components/academy-date-defaults";

export default function Page() {
  return <AcademyDateDefaults title="الإيرادات والتحصيلات" context="التقارير / التحصيلات المؤكدة"><FinancialReport /></AcademyDateDefaults>;
}
