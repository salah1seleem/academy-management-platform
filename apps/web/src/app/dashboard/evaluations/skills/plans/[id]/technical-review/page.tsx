import {PlayerTechnicalReviews} from "../../../../../../../features/player-technical-reviews";
export default async function Page({params}:{params:Promise<{id:string}>}){return <PlayerTechnicalReviews planId={(await params).id}/>;}
