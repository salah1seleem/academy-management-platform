import { PlayerDetails } from "../../../../features/player-record";
export default async function Page({ params }: { params: Promise<{ id: string }> }) { const { id } = await params; return <PlayerDetails id={id} />; }
