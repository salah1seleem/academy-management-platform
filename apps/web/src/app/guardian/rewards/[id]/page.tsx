import { Suspense } from "react";
import { notFound } from "next/navigation";
import { GuardianShell } from "../../../../components/guardian-shell";
import { LoadingState } from "../../../../components/dashboard-shell";
import { VipRewards } from "../../../../features/vip-rewards";
type Props={params:Promise<{id:string}>};
async function Reward({params}:Props){
  const {id}=await params;
  if(!/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id))notFound();
  return <VipRewards rewardId={id}/>;
}
export default function Page(props:Props){return <GuardianShell><Suspense fallback={<LoadingState/>}><Reward {...props}/></Suspense></GuardianShell>;}
