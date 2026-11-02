import { notFound } from "next/navigation";
import { StructureList } from "../../../../features/structure-list";
export default async function Page({ params }: { params: Promise<{ kind: string }> }) { const { kind } = await params; if (!["branches", "sports", "categories", "groups", "coaches"].includes(kind)) notFound(); return <StructureList kind={kind as "branches" | "sports" | "categories" | "groups" | "coaches"} />; }
