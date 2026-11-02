import { PlayerGoalEditor } from "../../../../../features/player-goals";
export default async function Page({ params }: { params: Promise<{ id: string }> }) { const { id } = await params; return <PlayerGoalEditor id={id} />; }
