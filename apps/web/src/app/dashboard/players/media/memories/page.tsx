import {MediaMemories,MediaMemoriesEntry} from "../../../../../features/media-memories";
export default async function Page({searchParams}:{searchParams:Promise<{playerId?:string;groupId?:string}>}){const p=await searchParams;return [p.playerId,p.groupId].some(id=>id&&/^[a-f0-9-]{36}$/i.test(id))?<MediaMemories playerId={p.playerId} groupId={p.groupId} family={false}/>:<MediaMemoriesEntry/>;}
