"use client";

import { useEffect } from "react";
import { useRouter } from "next/navigation";

type Session = { role: string };

export default function HomePage() {
  const router = useRouter();
  useEffect(() => {
    fetch("/api/v1/me")
      .then(async response => {
        if (response.status === 401) return router.replace("/login");
        if (!response.ok) throw new Error();
        const session = await response.json() as Session;
        router.replace(session.role === "Guardian" ? "/guardian" : "/dashboard");
      })
      .catch(() => router.replace("/login"));
  }, [router]);
  return <main className="center-shell"><p>جارٍ فتح الصفحة المناسبة لحسابك…</p></main>;
}
