import { SalaryHistory } from "../../../../../../features/salary-history";
export default async function Page({params}:{params:Promise<{id:string}>}){const {id}=await params;return <SalaryHistory id={id}/>;}
