import {MediaMemories} from "../../../../../features/media-memories";
export default async function Page({searchParams}:{searchParams:Promise<{playerId?:string;groupId?:string}>}){const p=await searchParams;return <MediaMemories playerId={p.playerId} groupId={p.groupId} family={false}/>;}
