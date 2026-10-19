"use client";
import Link from "next/link";
import {useEffect,useState} from "react";
import {PageHeader,LoadingState} from "../components/dashboard-shell";
const sections=[
 {id:"collections",label:"التحصيلات والمطالبات",permission:"reports.financial.read",links:[["التحصيلات والإيصالات","/dashboard/reports/financial"],["المطالبات والأرصدة","/dashboard/finance/receivables"],["طلبات الدفع","/dashboard/subscriptions/payments"]]},
 {id:"expenses",label:"المصروفات",permission:"reports.financial.read",links:[["المصروفات التشغيلية","/dashboard/finance/operating-expenses"]]},
 {id:"payroll",label:"الرواتب والمستحقات",permission:"payroll.read",links:[["كشف المستحقات والصرف","/dashboard/payroll"],["خصومات الموظفين","/dashboard/payroll/deductions"],["طلبات المبالغ الإضافية","/dashboard/payroll/requests"]]},
 {id:"private",label:"البرايفت والعمولات",permission:"payroll.read",links:[["اتفاقات وخدمات المدربين من الملف","/dashboard/academy/coaches"]]},
 {id:"agreements",label:"الاتفاقيات والتأجير",permission:"reports.financial.read",links:[["الاتفاقيات والتحصيل والتأجير","/dashboard/finance/external-agreements"]]},
 {id:"reports",label:"التقارير المالية",permission:"reports.financial.read",links:[["قائمة الأرباح والخسائر — أساس نقدي","/dashboard/finance/operating-statement"],["تقرير التحصيلات","/dashboard/reports/financial"],["القرارات المالية الموثقة","/dashboard/finance/decisions"]]}
];
export function FinanceWorkspace(){
 const [permissions,setPermissions]=useState<string[]|null>(null),[tab,setTab]=useState("collections"),[cash,setCash]=useState(false);
 useEffect(()=>{const c=new AbortController();fetch("/api/v1/my-permissions",{signal:c.signal,cache:"no-store"}).then(r=>r.ok?r.json():[]).then(setPermissions).catch(()=>setPermissions([]));fetch("/api/v1/academy/experience",{signal:c.signal,cache:"no-store"}).then(r=>r.ok?r.json():null).then(v=>setCash(v?.navigation?.cashEnabled===true)).catch(()=>{});return()=>c.abort();},[]);
 const visible=sections.filter(s=>permissions?.includes(s.permission));const active=visible.find(s=>s.id===tab)??visible[0];
 return <><PageHeader title="الحسابات والماليات" context="مساحة مالية موحدة"/>{!permissions?<LoadingState/>:!active?<p role="status">لا توجد أقسام مالية مخولة لهذا الحساب.</p>:<><nav className="workspace-tabs" aria-label="أقسام الماليات">{visible.map(s=><button key={s.id} aria-current={s.id===active.id?"page":undefined} onClick={()=>setTab(s.id)}>{s.label}</button>)}</nav><section className="list-card"><h2>{active.label}</h2><div className="workspace-destinations">{active.links.map(([label,href])=><Link key={href} href={href}>{label}<span aria-hidden="true">←</span></Link>)}</div></section>{active.id==="collections"&&<details className="detail-card"><summary>رقابة التحصيل والمطابقة</summary><div className="workspace-destinations">{cash&&<Link href="/dashboard/finance/cash-registers">الصناديق والعهد</Link>}<Link href="/dashboard/finance/provider-quarantine">اختلافات مزود الدفع</Link><Link href="/dashboard/finance/settlement-comparisons">مطابقة كشوف التحصيل</Link></div></details>}</>}</>;
}
