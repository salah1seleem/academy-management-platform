import { ProductPaymentReview } from "../../../../features/manual-payments";
export default async function Page({ params }: { params: Promise<{ id: string }> }) { return <ProductPaymentReview id={(await params).id} />; }
