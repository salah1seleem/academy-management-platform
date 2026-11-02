import{PlayerBadges}from"../../../../../features/player-badges";
export default async function Page({params}:{params:Promise<{playerId:string}>}){return <PlayerBadges playerId={(await params).playerId}/>;}
