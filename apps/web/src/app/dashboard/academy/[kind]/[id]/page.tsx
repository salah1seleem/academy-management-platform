import { notFound } from "next/navigation";
import { StructureDetails, StructureKind } from "../../../../../features/structure-record";
export default async function Page({ params }: { params: Promise<{ kind: string; id: string }> }) { const { kind, id } = await params; if (!["branches", "sports", "categories", "groups", "coaches"].includes(kind)) notFound(); return <StructureDetails kind={kind as StructureKind} id={id} />; }
