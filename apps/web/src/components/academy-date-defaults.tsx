"use client";

import { useSearchParams } from "next/navigation";
import { type ReactNode, useEffect, useState } from "react";
import { DateFilterDefaults, type DateRange } from "./date-filter-defaults";
import { ErrorState, LoadingState, PageHeader } from "./dashboard-shell";

export function AcademyDateDefaults({ title, context, requireBothDates = false, skip = false, children }: { title: string; context: string; requireBothDates?: boolean; skip?: boolean; children: ReactNode }) {
  const query = useSearchParams();
  const from = !!query.get("from"), to = !!query.get("to");
  const explicit = skip || query.get("allDates") === "true" || (requireBothDates ? from && to : from || to);
  const [attempt, setAttempt] = useState(0);
  const [result, setResult] = useState<DateRange | "failed" | "unavailable" | null>(null);
  const range = result && typeof result !== "string" ? result : null;

  useEffect(() => {
    if (explicit || range) return;
    const controller = new AbortController();
    const { signal } = controller;
    void (async () => {
      try {
        const response = await fetch("/api/v1/academy/calendar", { cache: "no-store", signal });
        if (signal.aborted) return;
        if (response.status === 401 || response.status === 403) {
          setResult("unavailable");
          return;
        }
        if (!response.ok) throw new Error();
        const { today } = await response.json() ?? {};
        if (typeof today !== "string" || !/^\d{4}-\d{2}-\d{2}$/.test(today)) throw new Error();
        const date = new Date(`${today}T00:00:00Z`);
        if (!Number.isFinite(date.valueOf()) || date.toISOString().slice(0, 10) !== today) throw new Error();
        if (!signal.aborted) setResult({ from: `${today.slice(0, 8)}01`, to: today });
      } catch {
        if (!signal.aborted) setResult("failed");
      }
    })();
    return () => controller.abort();
  }, [attempt, explicit, range]);

  if (explicit || range) return <DateFilterDefaults range={range}>{children}</DateFilterDefaults>;
  return <><PageHeader title={title} context={context} />
    {result === "unavailable" ? <ErrorState message="تاريخ الأكاديمية غير متاح لهذا الحساب." />
      : result === "failed" ? <section className="list-card">
        <ErrorState message="تعذر تحميل تاريخ الأكاديمية." />
        <button type="button" className="button button-secondary" onClick={() => { setResult(null); setAttempt(value => value + 1); }}>إعادة المحاولة</button>
      </section> : <LoadingState message="جارٍ تحميل تاريخ الأكاديمية…" />}
  </>;
}
