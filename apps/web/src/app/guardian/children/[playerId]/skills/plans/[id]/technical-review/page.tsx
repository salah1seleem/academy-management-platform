import {PlayerTechnicalReviews} from "../../../../../../../../features/player-technical-reviews";
export default async function Page({params}:{params:Promise<{playerId:string;id:string}>}){const p=await params;return <PlayerTechnicalReviews planId={p.id} playerId={p.playerId}/>;}
