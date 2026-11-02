import { GuardianSponsorDetail } from "../../../../features/guardian-sponsored";
export default async function Page({params}:{params:Promise<{id:string}>}){const {id}=await params;return <GuardianSponsorDetail id={id}/>;}
