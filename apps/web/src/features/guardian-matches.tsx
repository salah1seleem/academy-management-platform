"use client";

import { ArrowRight } from "lucide-react";
import Link from "next/link";
import { useEffect, useState } from "react";
import { GuardianShell } from "../components/guardian-shell";
import { isUpcomingMatch, MatchResultCard, type MatchCardValue } from "../components/match-result-card";
import { EmptyState, ErrorState, LoadingState } from "../components/dashboard-shell";

type Match = MatchCardValue & { id: string; isUpcoming: boolean };
const validId = (value: string) => /^[a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12}$/i.test(value);

export function GuardianMatches({ playerId, matchId }: { playerId?: string; matchId?: string }) {
  const [revision, setRevision] = useState(0);
  const query = new URLSearchParams();
  if (playerId) query.set("playerId", playerId);
  const back = matchId ? `/guardian/matches${query.size ? `?${query}` : ""}` : "/guardian#children";
  if (matchId) query.set("matchId", matchId);
  const path = `/api/v1/my-matches${query.size ? `?${query}` : ""}`;
  const key = `${path}:${revision}`;
  const valid = (!matchId || validId(matchId)) && (!playerId || validId(playerId));
  const [result, setResult] = useState<{ key: string; items?: Match[]; error?: boolean }>();
  useEffect(() => {
    if (!valid) return;
    const controller = new AbortController();
    fetch(path, { signal: controller.signal, cache: "no-store" })
      .then(async response => {
        if ([401, 403, 404].includes(response.status)) return [];
        if (!response.ok) throw new Error();
        const value: unknown = await response.json();
        if (!Array.isArray(value)) throw new Error();
        return value as Match[];
      })
      .then(items => {
        if (!controller.signal.aborted) setResult({
          key,
          items: matchId ? items.filter(item => item.id.toLowerCase() === matchId.toLowerCase()) : items,
        });
      })
      .catch(() => { if (!controller.signal.aborted) setResult({ key, error: true }); });
    return () => controller.abort();
  }, [key, matchId, path, valid]);

  const current = result?.key === key ? result : undefined;
  const upcoming = current?.items?.filter(isUpcomingMatch) ?? [];
  const history = current?.items?.filter(match => !isUpcomingMatch(match)) ?? [];

  return <GuardianShell>
    <header className="guardian-page-header guardian-match-header">
      <Link className="guardian-match-back" href={back}><ArrowRight size={18} aria-hidden />{matchId ? "العودة إلى المباريات" : "العودة إلى الأبناء"}</Link>
      <h1>{matchId ? "المباراة" : "المباريات"}</h1>
    </header>
    <div className="guardian-match-results">
      {!valid ? <EmptyState message="هذا الرابط غير متاح." />
        : !current ? <LoadingState message="جارٍ تحميل المباريات…" />
        : current.error ? <><ErrorState message="تعذر تحميل المباريات." /><button className="secondary-button" type="button" onClick={() => setRevision(v => v + 1)}>إعادة المحاولة</button></>
        : !current.items?.length ? <EmptyState message={matchId ? "المباراة غير متاحة في حسابك الحالي." : "لا توجد مباريات منشورة تخصك حاليًا."} />
        : matchId ? <MatchResultCard match={current.items[0]} />
        : <>
          {!!upcoming.length && <section aria-label="المباريات القادمة"><h2>المباريات القادمة</h2>{upcoming.map(match => <MatchResultCard key={match.id} match={match} />)}</section>}
          {!!history.length && <section aria-label="سجل المباريات"><h2>سجل المباريات</h2>{history.map(match => <MatchResultCard key={match.id} match={match} />)}</section>}
        </>}
    </div>
  </GuardianShell>;
}
