import {ExternalAgreementDetail} from "../../../../../features/external-agreements";
export default async function Page({params}:{params:Promise<{id:string}>}){const{id}=await params;return <ExternalAgreementDetail id={id}/>;}
