"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
export function WorkContextLink() {
  const [multiple, setMultiple] = useState(false);
  useEffect(() => { const controller = new AbortController(); void fetch("/api/v1/my-academies", { cache: "no-store", signal: controller.signal }).then(r => r.ok ? r.json() : []).then(data => { if (Array.isArray(data)) setMultiple(data.filter((c: { role: string }) => ["AcademyOwner", "AcademyAdmin"].includes(c.role)).length > 1); }).catch(() => {}); return () => controller.abort(); }, []);
  return multiple ? <Link className="work-context-link" href="/dashboard/context">تبديل سياق العمل</Link> : null;
}
