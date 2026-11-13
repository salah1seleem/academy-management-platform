import { AdminEnrollmentRequests } from "../../../features/guardian-core";
import { AcademyDateDefaults } from "../../../components/academy-date-defaults";

export default function Page() {
  return <AcademyDateDefaults title="طلبات الاشتراك الجديدة" context="اللاعبون / طلبات الاشتراك"><AdminEnrollmentRequests /></AcademyDateDefaults>;
}
