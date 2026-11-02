import {PlayerCertificate} from "../../../../../../features/player-certificates";
export default async function Page({params}:{params:Promise<{id:string}>}){return <PlayerCertificate sourceId={(await params).id}/>;}
