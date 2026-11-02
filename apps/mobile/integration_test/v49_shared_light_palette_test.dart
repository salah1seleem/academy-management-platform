import 'v48_owner_home_reports_test.dart' show runOwnerHomeReportAcceptance;
import 'v46_urgent_match_summary_test.dart' as guardian;

void main() {
  runOwnerHomeReportAcceptance(light: true);
  guardian.main();
}
