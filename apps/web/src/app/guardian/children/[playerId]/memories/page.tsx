import {MediaMemories} from "../../../../../features/media-memories";
export default async function Page({params}:{params:Promise<{playerId:string}>}){return <MediaMemories playerId={(await params).playerId}/>;}
