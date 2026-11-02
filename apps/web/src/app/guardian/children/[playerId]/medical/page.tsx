"use client";
import { use } from "react";
import { GuardianMedical } from "../../../../../features/guardian-content";
export default function Page({ params }: { params: Promise<{ playerId: string }> }) { const { playerId } = use(params); return <GuardianMedical playerId={playerId} />; }
