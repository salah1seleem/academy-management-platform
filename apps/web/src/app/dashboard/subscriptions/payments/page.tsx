import { AcademyDateDefaults } from "../../../../components/academy-date-defaults";
import { SubscriptionOperationalList } from "../../../../features/subscriptions";

export default function Page() {
  return <AcademyDateDefaults title="طلبات الدفع" context="مراجعة التحويلات" requireBothDates><SubscriptionOperationalList kind="payments" /></AcademyDateDefaults>;
}
