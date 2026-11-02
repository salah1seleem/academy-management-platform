import {ExternalAgreementDetail} from "../../../../../../../features/external-agreements";
export default async function Page({params}:{params:Promise<{id:string;receiptId:string}>}){const{id,receiptId}=await params;return <ExternalAgreementDetail id={id} receiptId={receiptId}/>;}
