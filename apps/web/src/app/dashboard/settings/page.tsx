import { AcademySettings } from "../../../features/academy-settings";
import { VersionedConfiguration } from "../../../features/versioned-configuration";
import Link from "next/link";
import {OwnerSettingsExtras} from "../../../features/experience-tools";
export default function Page() { return <><AcademySettings /><section className="detail-card"><h2>خصائص الرياضات</h2><p>إعدادات التشغيل بحسب الرياضة والفرع، بإدارة المالك.</p><Link href="/dashboard/settings/sports">ضبط خصائص الرياضات</Link></section><VersionedConfiguration /><OwnerSettingsExtras/></>; }
