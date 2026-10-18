import {TechnicalPromotion} from "../../../../../../features/technical-promotion";
export default async function Page({params}:{params:Promise<{id:string}>}){return <TechnicalPromotion id={(await params).id}/>;}
