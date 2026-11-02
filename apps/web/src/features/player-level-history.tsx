"use client";
export type LevelHistory = {items:{id:string;revision:number;action:string;fromLevelName:string|null;toLevelName:string|null;actorName:string;createdAtUtc:string;reviewedOn?:string;note?:string|null}[];hasMore:boolean};
const actions:Record<string,string>={Reviewed:"مراجعة المستوى المسجل",Changed:"اعتماد مستوى مراجع",Cleared:"إلغاء تحديد المستوى"};
export function PlayerLevelHistory({history,showPrivateNotes=false}:{history?:LevelHistory;showPrivateNotes?:boolean}){
 return <details className="placement-level"><summary>سجل مراجعات المستوى</summary>{!history?.items.length?<p>لا توجد مراجعات مؤرخة محفوظة بعد. لا نفترض تاريخ ترقية من المستوى المسجل وحده.</p>:history.items.map(r=><article key={r.id}><h3>{actions[r.action]}</h3><p>{r.fromLevelName??"لم يحدد"} ← {r.toLevelName??"لم يحدد"}</p><p><bdi>{r.reviewedOn??r.createdAtUtc.slice(0,10)}</bdi> · {r.actorName} · مراجعة{r.revision}</p>{showPrivateNotes&&r.note&&<p>{r.note}</p>}</article>)}{history?.hasMore&&<p>تظهر أحدث20 مراجعة؛ للسجل الأقدم راجع الإدارة المخولة.</p>}<p>تاريخ اعتماد يدوي محفوظ، وليس شهادة أو ترقية تلقائية. لم نعد إنشاء تواريخ قديمة غير موثقة.</p></details>;
}
