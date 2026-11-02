import {PayrollStatement} from "../../../../../features/payroll-statement";
export default async function Page({params,searchParams}:{params:Promise<{id:string}>;searchParams:Promise<{month?:string}>}){const {id}=await params;const {month}=await searchParams;return <PayrollStatement id={id} month={month??""}/>;}
