"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
import { CircleHelp } from "lucide-react";

export function ContextHelp({ lesson, guardian = false }: { lesson: string; guardian?: boolean }) {
  const [available, setAvailable] = useState(false);
  useEffect(() => { const c = new AbortController(); let generation = 0;
    async function load() { const attempt = ++generation; try { const r = await fetch(`/api/v1/help/lessons/${encodeURIComponent(lesson)}`, { signal: c.signal, cache: "no-store" }); const data = r.ok ? await r.json() : null; if (!c.signal.aborted && attempt === generation) setAvailable(data?.id === lesson); } catch { if (!c.signal.aborted && attempt === generation) setAvailable(false); } }
    void load(); window.addEventListener("focus", load); return () => { c.abort(); window.removeEventListener("focus", load); };
  }, [lesson]);
  return available ? <Link className="back-link" href={`${guardian ? "/guardian" : "/dashboard"}/help/${lesson}`}><CircleHelp size={18} aria-hidden="true"/>شرح هذه الخطوة</Link> : null;
}
