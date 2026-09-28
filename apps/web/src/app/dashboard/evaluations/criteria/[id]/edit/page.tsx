import { CriterionForm } from "../../../../../../features/evaluations";
export default async function Page({ params }: { params: Promise<{ id: string }> }) { const { id } = await params; return <CriterionForm id={id} />; }
