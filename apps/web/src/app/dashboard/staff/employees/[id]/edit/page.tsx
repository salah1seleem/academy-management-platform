import {EmployeeForm} from "../../../../../../features/employees";
export default async function Page({params}:{params:Promise<{id:string}>}){return <EmployeeForm id={(await params).id}/>;}
