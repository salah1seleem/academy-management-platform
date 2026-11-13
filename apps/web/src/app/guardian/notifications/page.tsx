import { GuardianShell } from "../../../components/guardian-shell";
import { NotificationInbox } from "../../../features/notification-center";

export default function Page() {
  return <GuardianShell>
    <header className="guardian-page-header"><h1>التنبيهات</h1></header>
    <NotificationInbox embedded guardian />
  </GuardianShell>;
}
