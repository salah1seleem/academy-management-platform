"use client";
import { use } from "react";
import { GuardianGallery } from "../../../../../features/guardian-content";
export default function Page({ params }: { params: Promise<{ playerId: string }> }) { const { playerId } = use(params); return <GuardianGallery playerId={playerId} />; }
