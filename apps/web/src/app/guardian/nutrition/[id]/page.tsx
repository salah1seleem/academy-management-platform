"use client";
import { use } from "react";
import { useSearchParams } from "next/navigation";
import { NutritionDetails } from "../../../../features/guardian-content";
export default function Page({ params }: { params: Promise<{ id: string }> }) { const { id } = use(params); const playerId = useSearchParams().get("player") ?? undefined; return <NutritionDetails id={id} playerId={playerId} />; }
