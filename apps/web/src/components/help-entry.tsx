"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
import { BookOpen } from "lucide-react";

export function HelpEntry({ guardian = false, current = false, onNavigate }: { guardian?: boolean; current?: boolean; onNavigate?: () => void }) {
  const [available, setAvailable] = useState(false);
  useEffect(() => {
    const c = new AbortController(); let generation = 0;
    async function load() {
      const request = ++generation; setAvailable(false);
      try {
        const r = await fetch("/api/v1/help/availability", { cache: "no-store", signal: c.signal });
        const result = r.ok ? await r.json() : null;
        if (!c.signal.aborted && request === generation) setAvailable(result?.available === true);
      } catch { /* Unavailable help never blocks the working application. */ }
    }
    void load(); window.addEventListener("focus", load);
    return () => { c.abort(); window.removeEventListener("focus", load); };
  }, []);
  return available ? <Link onClick={onNavigate} aria-current={current ? "page" : undefined} className={guardian ? "back-link" : "sidebar-security-link"} href={guardian ? "/guardian/help" : "/dashboard/help"}><BookOpen size={18} aria-hidden="true" />دليل الاستخدام</Link> : null;
}
