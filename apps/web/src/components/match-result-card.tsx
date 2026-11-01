import {ReactNode} from "react";
export type MatchCardValue={team:string;opponent:string;ourScore?:number|null;opponentScore?:number|null;resultFinalized?:boolean;status?:string;isActive?:boolean;publishedAtUtc?:string|null;matchDate:string;startTime:string;venue:string;tournament:string};
export function MatchResultCard({match:m,children}:{match:MatchCardValue;children?:ReactNode}){
  const final=(m.resultFinalized??m.status==="Completed")&&m.ourScore!=null&&m.opponentScore!=null&&m.isActive!==false&&m.status!=="Cancelled";
  const outcome=!final?"pending":m.ourScore!>m.opponentScore!?"win":m.ourScore!<m.opponentScore!?"loss":"draw";
  const state=m.isActive===false||m.status==="Cancelled"?"ملغاة":!final?m.ourScore!=null?"النتيجة قيد المراجعة":m.status==="Draft"?"مسودة":"قادمة":outcome==="win"?"فوز":outcome==="loss"?"خسارة":"تعادل";
  return <article className={`academy-match-card academy-match-card--${outcome}`}>
    <div className="academy-match-heading"><span>{m.tournament}</span><strong>{state}</strong></div>
    <div className="academy-match-score"><div><strong>{m.team}</strong><b>{m.ourScore??"—"}</b></div><span aria-hidden="true">×</span><div><strong>{m.opponent}</strong><b>{m.opponentScore??"—"}</b></div></div>
    <div className="academy-match-chips"><span>التاريخ: {m.matchDate}</span><span>التوقيت: {m.startTime.slice(0,5)}</span><span>المكان: {m.venue}</span></div>
    {children&&<div className="row-actions">{children}</div>}
  </article>;
}
