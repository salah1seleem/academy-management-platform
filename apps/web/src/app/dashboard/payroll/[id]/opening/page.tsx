import {PayrollOpening} from "../../../../../features/payroll-opening";
export default async function Page({params}:{params:Promise<{id:string}>}){return <PayrollOpening id={(await params).id}/>;}
