"use client";
import { useParams } from "next/navigation";
import { RenewalDiscountDetail } from "../../../../../features/subscriptions";
export default function Page() { return <RenewalDiscountDetail id={String(useParams().id)} />; }
