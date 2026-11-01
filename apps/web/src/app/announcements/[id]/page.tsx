import { AudienceAnnouncement } from "../../../features/audience-communication";
export default async function Page({ params }: { params: Promise<{ id: string }> }) { const { id } = await params; return <main className="page-shell" dir="rtl"><AudienceAnnouncement key={id} id={id}/></main>; }
