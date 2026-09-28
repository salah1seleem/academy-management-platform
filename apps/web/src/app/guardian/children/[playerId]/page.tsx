import { ChildProfile } from "../../../../features/guardian-core";
export default async function Page({ params }: { params: Promise<{ playerId: string }> }) { const { playerId } = await params; return <ChildProfile playerId={playerId} />; }
