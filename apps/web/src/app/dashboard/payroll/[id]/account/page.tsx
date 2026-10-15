import {PayrollAccount} from "../../../../../features/payroll-account";
export default async function Page({params}:{params:Promise<{id:string}>}){return <PayrollAccount id={(await params).id}/>;}
