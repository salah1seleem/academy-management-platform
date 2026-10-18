import {GuardianCertificates} from "../../../../../features/player-certificates";
export default async function Page({params}:{params:Promise<{playerId:string}>}){return <GuardianCertificates playerId={(await params).playerId}/>;}
