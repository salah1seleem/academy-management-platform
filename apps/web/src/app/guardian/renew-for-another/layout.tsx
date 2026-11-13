"use client";

import { useRouter } from "next/navigation";
import { useEffect, useState, type ReactNode } from "react";
import { ErrorState, LoadingState } from "../../../components/dashboard-shell";

export default function Layout({ children }: { children: ReactNode }) {
  const router = useRouter();
  const [status, setStatus] = useState<"loading" | "ready" | "failed">("loading");
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    const request = new AbortController();
    void fetch("/api/v1/me", { cache: "no-store", signal: request.signal }).then(async response => {
      if (request.signal.aborted) return;
      if (response.status === 401 || response.status === 403) {
        router.replace("/guardian/sign-in");
        return;
      }
      if (!response.ok) throw new Error();
      const session = await response.json() as { role?: string };
      if (request.signal.aborted) return;
      if (session.role === "Guardian") setStatus("ready");
      else router.replace("/guardian/sign-in");
    }).catch(() => {
      if (!request.signal.aborted) setStatus("failed");
    });
    return () => request.abort();
  }, [attempt, router]);

  if (status === "ready") return children;
  return <main className="center-shell guardian-renewal-shell">
    <section className="authenticated-card guardian-renewal-card" aria-label="دخول ولي الأمر">
      {status === "loading" ? <LoadingState message="جارٍ التحقق من تسجيل الدخول…" /> : <>
        <ErrorState message="تعذر التحقق من تسجيل الدخول. أعد المحاولة." />
        <button type="button" className="secondary-button" onClick={() => {
          setStatus("loading");
          setAttempt(value => value + 1);
        }}>إعادة المحاولة</button>
      </>}
    </section>
  </main>;
}
