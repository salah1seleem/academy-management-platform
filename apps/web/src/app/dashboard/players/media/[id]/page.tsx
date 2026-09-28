"use client";
import { use } from "react"; import { ContentForm } from "../../../../../features/admin-content";
export default function Page({ params }: { params: Promise<{ id: string }> }) { return <ContentForm kind="media" id={use(params).id} readOnly />; }
