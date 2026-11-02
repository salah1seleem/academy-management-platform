import { GuardianPlayerSkills } from "../../../../../features/player-skills";
export default async function Page({params}:{params:Promise<{playerId:string}>}){const {playerId}=await params;return <GuardianPlayerSkills playerId={playerId}/>;}
