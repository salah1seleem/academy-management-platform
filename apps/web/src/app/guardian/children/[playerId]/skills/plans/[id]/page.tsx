import { PlayerSkillPlan } from "../../../../../../../features/player-skills";
export default async function Page({params}:{params:Promise<{playerId:string;id:string}>}){const {playerId,id}=await params;return <PlayerSkillPlan playerId={playerId} id={id}/>;}
