"use client";
import { use } from "react";
import { NutritionLibrary } from "../../../../../features/guardian-content";
export default function Page({ params }: { params: Promise<{ playerId: string }> }) { const { playerId } = use(params); return <NutritionLibrary playerId={playerId} />; }
