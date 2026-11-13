import { CalendarDays, MapPin, Shield, Trophy } from "lucide-react";
import type { ReactNode } from "react";
import { formatDateAr, formatTimeAr } from "../lib/formatters";

export type MatchCardValue = {
  team: string;
  opponent: string;
  ourScore?: number | null;
  opponentScore?: number | null;
  resultFinalized?: boolean | null;
  status?: string;
  isActive?: boolean;
  isUpcoming?: boolean;
  publishedAtUtc?: string | null;
  matchDate: string;
  startTime: string;
  venue: string;
  branch?: string;
  tournament: string;
  notes?: string | null;
};

function matchResult(match: MatchCardValue) {
  const cancelled = match.isActive === false || match.status === "Cancelled";
  const hasScores = match.ourScore != null && match.opponentScore != null;
  const final = !cancelled && match.status !== "Draft" && match.status !== "ResultDraft" && hasScores
    && (match.resultFinalized ?? match.status === "Completed");
  return { cancelled, hasScores, final };
}

export function isUpcomingMatch(match: MatchCardValue) {
  const { cancelled, final } = matchResult(match);
  return match.isUpcoming === true && !cancelled && !final;
}

export function MatchResultCard({ match, children, showDraftScores = false }: {
  match: MatchCardValue;
  children?: ReactNode;
  showDraftScores?: boolean;
}) {
  const { cancelled, hasScores, final } = matchResult(match);
  const outcome = !final ? null : match.ourScore! > match.opponentScore! ? "win"
    : match.ourScore! < match.opponentScore! ? "loss" : "draw";
  const state = cancelled ? "ملغاة" : outcome ? { win: "فوز", loss: "خسارة", draw: "تعادل" }[outcome]
    : match.status === "Draft" ? "مسودة"
    : hasScores || match.status === "ResultDraft" || match.status === "Completed" ? "النتيجة قيد المراجعة"
    : match.isUpcoming === false ? "بانتظار النتيجة" : "مجدولة";
  const scoresVisible = !cancelled && (final || showDraftScores && hasScores);
  const place = [...new Set([match.venue, match.branch].filter(Boolean))].join(" · ");
  const note = match.notes?.trim();
  const kickoff = formatTimeAr(match.startTime);

  return <article className={`academy-match-card academy-match-card--${outcome ?? "pending"}`}>
    <header className="academy-match-heading">
      <span><Trophy size={16} aria-hidden /> {match.tournament}</span>
      <strong>{state}</strong>
    </header>
    <div className="academy-match-score">
      {[[match.team, match.ourScore], [match.opponent, match.opponentScore]].map(([team, score], index) => <div key={index}
        role={scoresVisible ? "group" : undefined} aria-label={scoresVisible ? `${team}: ${score}` : undefined}>
        {scoresVisible ? <b aria-hidden="true">{score}</b> : <Shield size={25} aria-hidden />}
        <strong aria-hidden={scoresVisible || undefined}>{team}</strong>
      </div>)}
    </div>
    <div className="academy-match-chips">
      <span><CalendarDays size={15} aria-hidden />
        <time dateTime={match.matchDate}>{formatDateAr(match.matchDate)}</time>
        {!scoresVisible && !cancelled && kickoff !== "—" && <> · <bdi>{kickoff}</bdi></>}
      </span>
      {place && <span><MapPin size={15} aria-hidden /> {place}</span>}
    </div>
    {note && (isUpcomingMatch(match) && !scoresVisible
      ? <p className="academy-match-note">{note}</p>
      : <details className="academy-match-note"><summary>ملاحظات المباراة</summary><p>{note}</p></details>)}
    {children && <div className="row-actions">{children}</div>}
  </article>;
}
