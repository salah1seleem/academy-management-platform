import {PlayerMonthly} from "../../../../../features/player-monthly";
export default async function Page({params}:{params:Promise<{playerId:string}>}){return <PlayerMonthly playerId={(await params).playerId}/>;}
