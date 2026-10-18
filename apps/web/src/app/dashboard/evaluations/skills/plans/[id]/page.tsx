import { PlayerSkillPlan } from "../../../../../../features/player-skills";
export default async function Page({params}:{params:Promise<{id:string}>}){const {id}=await params;return <PlayerSkillPlan key={id} id={id}/>;}
