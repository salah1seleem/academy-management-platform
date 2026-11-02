import { TournamentPaymentReview } from "../../../../features/matches";
export default async function Page({ params }: { params: Promise<{ id: string }> }) { return <TournamentPaymentReview id={(await params).id} />; }
