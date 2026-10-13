"use client";
import { useParams } from "next/navigation";
import { FamilyRenewalRequest } from "../../../../features/family-renewal-request";
export default function Page() { return <FamilyRenewalRequest id={String(useParams().id)} staff />; }
