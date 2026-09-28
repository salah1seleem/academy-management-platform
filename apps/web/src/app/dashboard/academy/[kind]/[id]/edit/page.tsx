import { notFound } from "next/navigation";
import { StructureEdit, StructureKind } from "../../../../../../features/structure-record";
export default async function Page({ params }: { params: Promise<{ kind: string; id: string }> }) { const { kind, id } = await params; if (!["branches", "sports", "categories", "groups"].includes(kind)) notFound(); return <StructureEdit kind={kind as Exclude<StructureKind, "coaches">} id={id} />; }
