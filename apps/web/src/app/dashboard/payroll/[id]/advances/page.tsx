import {PayrollAdvances} from "../../../../../features/payroll-advances";
export default async function Page({params}:{params:Promise<{id:string}>}){return <PayrollAdvances id={(await params).id}/>;}
