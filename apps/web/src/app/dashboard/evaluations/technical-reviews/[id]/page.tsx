import {TechnicalReviewDetail} from "../../../../../features/player-technical-reviews";
export default async function Page({params}:{params:Promise<{id:string}>}){return <TechnicalReviewDetail id={(await params).id}/>;}
