import { PageHeader } from "../../../components/dashboard-shell";
import { WorkContextChooser } from "../../../features/work-context";
export default function WorkContextPage() { return <><PageHeader title="اختر سياق العمل" context="الحساب والصلاحيات" description="كل سياق له صلاحيات وفروع ورياضات مستقلة." /><WorkContextChooser /></>; }
