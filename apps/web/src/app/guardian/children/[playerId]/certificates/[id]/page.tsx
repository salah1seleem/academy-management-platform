import {PlayerCertificate} from "../../../../../../features/player-certificates";
export default async function Page({params}:{params:Promise<{playerId:string;id:string}>}){const p=await params;return <PlayerCertificate playerId={p.playerId} id={p.id}/>;}
