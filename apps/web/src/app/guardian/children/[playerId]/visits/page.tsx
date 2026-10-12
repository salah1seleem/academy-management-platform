import { GuardianVisits } from "../../../../../features/guardian-visits";
export default async function Page({params}:{params:Promise<{playerId:string}>}) { const {playerId}=await params;return <GuardianVisits playerId={playerId}/>; }
