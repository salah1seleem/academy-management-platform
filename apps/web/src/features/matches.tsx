"use client";
import { MatchResultCard } from "../components/match-result-card";
import { AudienceCommunication } from "./audience-communication";
import {MatchCopyDialog,MatchEditor,MatchEditValue} from "./match-editor";
import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { useSearchParams } from "next/navigation";
import { AdminDataTable, ConfirmationDialog, ErrorState, LoadingState, PageHeader, StatusBadge } from "../components/dashboard-shell";
import { paymentQueueBack } from "../lib/payment-queue-navigation";
import { formatMoneyAr } from "../lib/formatters";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
type Option = { id: string; arabicName: string; sportId?: string; branchId?: string; ageCategoryId?: string; minimumBirthYear?: number; maximumBirthYear?: number };
type Tournament = { id: string; name: string; sportId: string; fee: number; currency: string; isActive: boolean };
type Team = { id: string; name: string; sportId: string; branchId: string; ageCategoryId: string; coachMembershipId: string; enrollmentIds: string[]; isActive: boolean;coachCanManageMatches?:boolean;sportKind?:string };
type Player = { id: string; playerId: string; name: string; birthYear: number; sportId: string; trainingGroupId: string };
type Match = MatchEditValue & { tournament:string;team:string };
type Options = { canAdd?:boolean;canEdit?:boolean;canCopy?:boolean;tournaments: Tournament[]; teams: Team[]; players: Player[]; coaches: { id: string; name: string }[] };
const initialTeam: Team = { id: "", name: "", sportId: "", branchId: "", ageCategoryId: "", coachMembershipId: "", enrollmentIds: [], isActive: true };
export function MatchesWorkspace() {
  const requestKeys=useRef(new Map<string,string>());
  const incoming = useSearchParams(); const matchId = incoming.get("matchId");
  const [tab,setTab]=useState(["tournaments","teams","matches"].includes(incoming.get("tab")??"")?incoming.get("tab")!:"tournaments");
  const [editor,setEditor]=useState<"tournament"|"team"|"match"|null>(null);
  const [scope,setScope]=useState({sport:"",age:"",group:"",status:"",search:""});
  const [options, setOptions] = useState<Options | null>(null); const [structure, setStructure] = useState<{ sports: Option[]; branches: Option[]; ageCategories?: Option[]; categories?: Option[]; groups: Option[] }>({ sports: [], branches: [], categories: [], groups: [] });
  const [items, setItems] = useState<Match[]>([]); const [team, setTeam] = useState<Team>(initialTeam); const [tournament, setTournament] = useState<Tournament | null>(null); const [match, setMatch] = useState<Match | null>(null); const [filters, setFilters] = useState({ tournamentId: incoming.get("tournamentId")??"", teamId: incoming.get("teamId")??"", from:incoming.get("from")??"",to:incoming.get("to")??"" }); const [message, setMessage] = useState(""); const [busy, setBusy] = useState(false);
  const [copyId,setCopyId]=useState<string|null>(null),[editorRevision,setEditorRevision]=useState(0),[template,setTemplate]=useState<{tournamentId:string;teamId:string;venue:string}|null>(null);
  async function copied(id:string){const r=await fetch(`/api/v1/manage/matches/${id}`,{cache:"no-store"});if(!r.ok)throw new Error();setMatch(await r.json());setTab("matches");setEditor("match");setTemplate(null);setEditorRevision(v=>v+1);setCopyId(null);setMessage("تم إنشاء مسودة مستقلة دون نشر أو إشعار. راجع الموعد والمشاركين قبل النشر.");setFilters({tournamentId:"",teamId:"",from:"",to:""});requestAnimationFrame(()=>document.getElementById("match-editor")?.scrollIntoView({behavior:"smooth"}));}
  const query = new URLSearchParams(Object.entries(filters).filter(([, v]) => v)).toString();
  const load = useCallback(() => Promise.all([fetch("/api/v1/manage/matches/options").then(r => r.ok ? r.json() : Promise.reject()), fetch(`/api/v1/manage/matches/?${query}`).then(r => r.ok ? r.json() : Promise.reject())]).then(([o, m]) => { setOptions(o); setItems(m); }).catch(() => setMessage("تعذر تحميل المباريات والبطولات.")), [query]);
  useEffect(() => { void load(); fetch("/api/v1/manage/structure/options").then(r => r.ok ? r.json() : Promise.reject()).then(setStructure).catch(() => undefined); }, [load]);
  useEffect(() => {
    if (!matchId) return;
    const abort = new AbortController();
    fetch(`/api/v1/manage/matches/${encodeURIComponent(matchId)}`, { cache: "no-store", signal: abort.signal }).then(async r => {
      if (!r.ok) throw new Error();
      const value = await r.json() as Match;
      if (!abort.signal.aborted) { setMatch(value);setTab("matches");setEditor("match"); setTemplate(null); setEditorRevision(v => v + 1); requestAnimationFrame(() => document.getElementById("match-editor")?.scrollIntoView()); }
    }).catch(() => { if (!abort.signal.aborted) setMessage("تعذر فتح المباراة أو أنها خارج صلاحياتك."); });
    return () => abort.abort();
  }, [matchId]);
  async function write(path: string, body: unknown, edit: boolean) { setBusy(true); try { await csrfRequest(path, edit ? "PUT" : "POST", body); setMessage("تم الحفظ بنجاح."); await load(); return true; } catch(e) { setMessage(e instanceof Error ? e.message : "تعذر الحفظ."); return false; } finally { setBusy(false); } }
  async function saveTournament(e: FormEvent<HTMLFormElement>) { e.preventDefault(); if (busy) return; const f = new FormData(e.currentTarget); const saved = await write(`/api/v1/manage/matches/tournaments${tournament ? `/${tournament.id}` : ""}`, { name: f.get("name"), sportId: f.get("sport"), fee: Number(f.get("fee")), currency: "EGP", isActive: f.get("active") === "on" }, !!tournament); if (saved) {setTournament(null);setEditor(null);} }
  async function saveTeam(e: FormEvent) { e.preventDefault(); if (busy) return; if (await write(`/api/v1/manage/matches/teams${team.id ? `/${team.id}` : ""}`, team, !!team.id)) {setTeam(initialTeam);setEditor(null);} }
  async function saveMatch(e: FormEvent<HTMLFormElement>) { e.preventDefault(); if (busy) return; const f = new FormData(e.currentTarget); const submitter = (e.nativeEvent as SubmitEvent).submitter as HTMLButtonElement; const signature=JSON.stringify(Array.from(f.entries()));if(!requestKeys.current.has(signature))requestKeys.current.set(signature,crypto.randomUUID()); const body = { requestId:match?null:requestKeys.current.get(signature),resultVersion:match?.resultRevision,resultFinalized:f.get("resultFinalized")==="on"&&f.get("ourScore")!==""&&f.get("opponentScore")!==""&&(!!match?.publishedAtUtc||submitter?.value==="publish"),goalSlots:options?.teams.find(t=>t.id===f.get("team"))?.sportKind==="Football"?Array.from({length:Number(f.get("ourScore"))||0},(_,i)=>{const v=String(f.get(`goal-${i+1}`)??"Unknown");return {ordinal:i+1,kind:v==="Unknown"||v==="OwnGoal"?v:"Player",playerId:v==="Unknown"||v==="OwnGoal"?null:v};}):[], tournamentId: f.get("tournament"), teamId: f.get("team"), opponent: f.get("opponent"), matchDate: f.get("date"), startTime: `${f.get("time")}:00`, venue: f.get("venue"), notes: f.get("notes"), ourScore: f.get("ourScore") === "" ? null : Number(f.get("ourScore")), opponentScore: f.get("opponentScore") === "" ? null : Number(f.get("opponentScore")), benchPlayerIds: f.getAll("bench"), publish: submitter?.value === "publish", isActive: f.get("active") === "on", rosterQuoteHash: f.get("rosterQuoteHash") || null, coachMembershipId: f.get("coachMembershipId") || null, coachAssignmentVersion: Number(f.get("coachAssignmentVersion") ?? 0), coachAssignmentNote: f.get("coachAssignmentNote") || null }; if (await write(`/api/v1/manage/matches/${match?.id ?? ""}`, body, !!match)) {setTemplate(submitter?.value === "another" ? {tournamentId:String(body.tournamentId),teamId:String(body.teamId),venue:String(body.venue)} : null);setMatch(null);setEditor(submitter?.value === "another"?"match":null);setEditorRevision(v=>v+1);setMessage(submitter?.value === "another" ? "تم حفظ المباراة. أدخل موعد ومنافس المباراة التالية." : "تم حفظ المباراة بنجاح.");setFilters({tournamentId:String(body.tournamentId),teamId:String(body.teamId),from:String(body.matchDate),to:String(body.matchDate)});} }
  const categories = structure.ageCategories ?? structure.categories ?? []; const category = categories.find(x => x.id === team.ageCategoryId);
  const roster = options?.players.filter(x => x.sportId === team.sportId && (!category?.minimumBirthYear || x.birthYear >= category.minimumBirthYear) && (!category?.maximumBirthYear || x.birthYear <= category.maximumBirthYear)) ?? [];

  const visibleTeams=options?.teams.filter(t=>(!scope.sport||t.sportId===scope.sport)&&(!scope.age||t.ageCategoryId===scope.age)&&(!scope.group||options.players.some(p=>p.trainingGroupId===scope.group&&t.enrollmentIds.includes(p.id)))&&(!scope.status||String(t.isActive)===scope.status)&&t.name.includes(scope.search))??[];
  const visibleTournaments=options?.tournaments.filter(t=>(!scope.sport||t.sportId===scope.sport)&&(!scope.status||String(t.isActive)===scope.status)&&t.name.includes(scope.search))??[];
  const visibleMatches=items.filter(m=>{const t=options?.teams.find(t=>t.id===m.teamId);return (!scope.sport||t?.sportId===scope.sport)&&(!scope.age||t?.ageCategoryId===scope.age)&&(!scope.group||options?.players.some(p=>p.trainingGroupId===scope.group&&t?.enrollmentIds.includes(p.id)))&&(!scope.status || scope.status === (!m.isActive ? "Cancelled" : !m.publishedAtUtc ? "Draft" : m.resultFinalized ? "Completed" : m.ourScore != null ? "Draft" : "Published"))&&`${m.team} ${m.opponent} ${m.tournament}`.includes(scope.search);});
  const selectTab=(value:string)=>{setTab(value);setEditor(null);setScope({sport:"",age:"",group:"",status:"",search:""});};
  const startNew=()=>{setTournament(null);setTeam(initialTeam);setMatch(null);setTemplate(null);setEditorRevision(v=>v+1);setEditor(tab==="tournaments"?"tournament":tab==="teams"?"team":"match");};
  return <>
    <PageHeader title="البطولات والمباريات" context="عمليات الأكاديمية" />
    <nav className="workspace-tabs" aria-label="أقسام البطولات والمباريات">{[["tournaments","البطولات"],["teams","الفرق والمنتخبات"],["matches","المباريات"]].map(([value,label])=><button key={value} type="button" aria-current={tab===value?"page":undefined} onClick={()=>selectTab(value)}>{label}</button>)}</nav>
    {message&&<p role="status">{message}</p>}
    {!options?(message?<ErrorState message={message}/>:<LoadingState/>):<div className="match-workspace">
      {editor?<section className="detail-card"><button type="button" className="back-link" onClick={()=>setEditor(null)}>← العودة إلى القائمة</button>
        {editor==="tournament"?<form className="form-shell" key={tournament?.id ?? "new-tournament"} onSubmit={e => void saveTournament(e)}><h2>{tournament ? "تعديل البطولة" : "إنشاء بطولة"}</h2><label>اسم البطولة<input name="name" required maxLength={120} defaultValue={tournament?.name} /></label><label>الرياضة<select name="sport" required defaultValue={tournament?.sportId ?? ""}><option value="">اختر الرياضة</option>{structure.sports.map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select></label><label>رسم المشاركة الإضافي (جنيه)<input name="fee" type="number" min="0" step="0.01" required defaultValue={tournament?.fee ?? 0} /></label><p>رسم مستقل لا يمدد اشتراك التدريب. الصفر يعني بطولة بلا رسم.</p><label><input name="active" type="checkbox" defaultChecked={tournament?.isActive ?? true} />فعالة</label><button disabled={busy}>حفظ البطولة</button>{tournament && <button type="button" onClick={() => setTournament(null)}>بطولة جديدة</button>}</form>:editor==="team"?<><h2>{team.id?"تعديل الفريق":"إنشاء فريق أو منتخب"}</h2><form className="form-shell" onSubmit={e => void saveTeam(e)}><label>البدء من مجموعة موجودة<select defaultValue="" onChange={e => { const g = structure.groups.find(x => x.id === e.target.value); if (g) setTeam({ ...initialTeam, name: g.arabicName, sportId: g.sportId!, branchId: g.branchId!, ageCategoryId: g.ageCategoryId!, enrollmentIds: options.players.filter(p => p.trainingGroupId === g.id).map(p => p.id) }); }}><option value="">اختياري — أو أنشئ منتخبًا مستقلًا</option>{structure.groups.map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select></label><label>اسم الفريق / المنتخب<input required maxLength={120} value={team.name} onChange={e => setTeam({ ...team, name: e.target.value })} placeholder="مثال: منتخب 2013" /></label>{[["الرياضة", "sportId", structure.sports], ["الفرع", "branchId", structure.branches], ["الفئة العمرية", "ageCategoryId", categories]].map(([name, key, choices]) => <label key={key as string}>{name as string}<select required value={team[key as "sportId"]} onChange={e => setTeam({ ...team, [key as string]: e.target.value, enrollmentIds: [] })}><option value="">اختر</option>{(choices as Option[]).map(x => <option value={x.id} key={x.id}>{x.arabicName}</option>)}</select></label>)}<label>مدرب الفريق<select required value={team.coachMembershipId} onChange={e => setTeam({ ...team, coachMembershipId: e.target.value })}><option value="">اختر المدرب</option>{options.coaches.map(x => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label><label><input type="checkbox" checked={team.coachCanManageMatches??false} onChange={e=>setTeam({...team,coachCanManageMatches:e.target.checked})}/>{"\u0627\u0644\u0633\u0645\u0627\u062d \u0644\u0645\u062f\u0631\u0628 \u0627\u0644\u0641\u0631\u064a\u0642 \u0628\u0625\u062f\u0627\u0631\u0629 \u0645\u0628\u0627\u0631\u064a\u0627\u062a\u0647 \u0648\u0646\u062a\u0627\u0626\u062c\u0647"}</label><div className="team-roster" aria-label="قائمة المنتخب">{roster.map(p => <label key={p.id}><input type="checkbox" checked={team.enrollmentIds.includes(p.id)} onChange={e => setTeam({ ...team, enrollmentIds: e.target.checked ? [...team.enrollmentIds, p.id] : team.enrollmentIds.filter(id => id !== p.id) })} />{p.name} · {p.birthYear}</label>)}</div><label><input type="checkbox" checked={team.isActive} onChange={e => setTeam({ ...team, isActive: e.target.checked })} />الفريق فعال</label><button disabled={busy || !team.enrollmentIds.length}>حفظ الفريق</button><button type="button" onClick={() => setTeam(initialTeam)}>فريق جديد</button></form>{team.id&&<AudienceCommunication id={team.id} team/>}</>:<MatchEditor key={`${match?.id??"new"}-${editorRevision}`} match={match} template={template} options={options} busy={busy} onSubmit={e=>void saveMatch(e)} onNew={()=>{setMatch(null);setTemplate(null);setEditorRevision(v=>v+1);}}/>}
      </section>:<>
        <div className="report-filters"><label>البحث بالاسم<input value={scope.search} onChange={e=>setScope({...scope,search:e.target.value})}/></label>
        <label>الرياضة<select value={scope.sport} onChange={e=>setScope({...scope,sport:e.target.value})}><option value="">كل الرياضات</option>{structure.sports.map(s=><option key={s.id} value={s.id}>{s.arabicName}</option>)}</select></label>
        {tab!=="tournaments"&&<><label>الفئة العمرية<select value={scope.age} onChange={e=>setScope({...scope,age:e.target.value})}><option value="">كل الفئات</option>{categories.map(s=><option key={s.id} value={s.id}>{s.arabicName}</option>)}</select></label><label>المجموعة<select value={scope.group} onChange={e=>setScope({...scope,group:e.target.value})}><option value="">كل المجموعات</option>{structure.groups.map(s=><option key={s.id} value={s.id}>{s.arabicName}</option>)}</select></label></>}
        {tab!=="matches"&&<label>الحالة<select value={scope.status} onChange={e=>setScope({...scope,status:e.target.value})}><option value="">كل الحالات</option><option value="true">فعالة</option><option value="false">موقوفة</option></select></label>}
        {tab==="matches"&&<><label>البطولة<select value={filters.tournamentId} onChange={e=>setFilters({...filters,tournamentId:e.target.value})}><option value="">كل البطولات</option>{options.tournaments.map(t=><option key={t.id} value={t.id}>{t.name}</option>)}</select></label><label>الفريق<select value={filters.teamId} onChange={e=>setFilters({...filters,teamId:e.target.value})}><option value="">كل الفرق</option>{options.teams.map(t=><option key={t.id} value={t.id}>{t.name}</option>)}</select></label><label>من تاريخ<input type="date" value={filters.from} onChange={e=>setFilters({...filters,from:e.target.value})}/></label><label>إلى تاريخ<input type="date" value={filters.to} onChange={e=>setFilters({...filters,to:e.target.value})}/></label></>}
        <button onClick={()=>{setScope({sport:"",age:"",group:"",status:"",search:""});setFilters({tournamentId:"",teamId:"",from:"",to:""});}}>مسح</button>{(tab!=="matches"||options.canAdd!==false)&&<button className="primary-button" onClick={startNew}>{tab==="tournaments"?"إنشاء بطولة":tab==="teams"?"إنشاء فريق / منتخب":"إنشاء مباراة"}</button>}</div>
        {tab === "matches" && <nav className="workspace-tabs" aria-label="حالة المباريات">
          {[["", "الكل"], ["Published", "المجدولة"], ["Completed", "النتائج"], ["Draft", "قيد المراجعة"], ["Cancelled", "الملغاة"]].map(([value, label]) => <button
            key={value} type="button" aria-pressed={scope.status === value} onClick={() => setScope({ ...scope, status: value })}
          >{label}</button>)}
        </nav>}
        <section className="list-card">{tab==="tournaments"?<AdminDataTable label="البطولات" rows={visibleTournaments} rowKey={t=>t.id} columns={[{key:"name",header:"البطولة",primary:true,render:t=>t.name},{key:"sport",header:"الرياضة",render:t=>structure.sports.find(s=>s.id===t.sportId)?.arabicName??"—"},{key:"fee",header:"رسم المشاركة",render:t=>`${t.fee} ${t.currency}`},{key:"status",header:"الحالة",render:t=>t.isActive?"فعالة":"موقوفة"},{key:"action",header:"الإجراء",render:t=><button onClick={()=>{setTournament(t);setEditor("tournament");}}>عرض / تعديل</button>}]}/>
        :tab==="teams"?<AdminDataTable label="الفرق والمنتخبات" rows={visibleTeams} rowKey={t=>t.id} columns={[{key:"name",header:"الفريق",primary:true,render:t=>t.name},{key:"age",header:"الفئة العمرية",render:t=>categories.find(a=>a.id===t.ageCategoryId)?.arabicName??"—"},{key:"coach",header:"المدرب",render:t=>options.coaches.find(c=>c.id===t.coachMembershipId)?.name??"—"},{key:"count",header:"اللاعبون",render:t=>t.enrollmentIds.length},{key:"action",header:"الإجراء",render:t=><button onClick={()=>{setTeam(t);setEditor("team");}}>عرض / تعديل</button>}]}/>
        :<div aria-label="المباريات">{visibleMatches.length?visibleMatches.map(m=><MatchResultCard key={m.id} match={m} showDraftScores={options.canEdit!==false}>{options.canEdit!==false&&<button onClick={()=>{setMatch(m);setTemplate(null);setEditorRevision(v=>v+1);setEditor("match");}}>{"\u0639\u0631\u0636 / \u062a\u0639\u062f\u064a\u0644 \u0627\u0644\u0646\u062a\u064a\u062c\u0629"}</button>}{options.canCopy&&<button onClick={()=>setCopyId(m.id)}>{"\u0646\u0633\u062e \u0645\u0628\u0627\u0631\u0627\u0629"}</button>}</MatchResultCard>):<p>{"\u0644\u0627 \u062a\u0648\u062c\u062f \u0645\u0628\u0627\u0631\u064a\u0627\u062a \u0636\u0645\u0646 \u0627\u0644\u0641\u0644\u0627\u062a\u0631."}</p>}</div>}</section>
      </>}
    </div>}
    {copyId&&<MatchCopyDialog key={copyId} id={copyId} onCancel={()=>setCopyId(null)} onCreated={copied}/>}
  </>;
}
type TournamentPayment = {
  id: string;
  tournamentName: string;
  player: string;
  amount: number;
  currency: string;
  status: "Pending" | "Confirmed" | "Rejected";
  hasProof: boolean;
  proofAssetId: string | null;
  proofUrl: string | null;
  receiptNumber: string | null;
  reviewReason: string | null;
  canReview: boolean;
};
type TournamentDecision = { accept: boolean; reason: string | null; expectedProofAssetId: string | null };
const tournamentRequestTimeoutMs = 12_000;

async function readTournamentPayment(path: string, id: string, signal?: AbortSignal) {
  const deadline = AbortSignal.timeout(tournamentRequestTimeoutMs);
  const response = await fetch(path, { cache: "no-store", signal: signal ? AbortSignal.any([signal, deadline]) : deadline });
  if (!response.ok)
    throw new DashboardRequestError([401, 403, 404].includes(response.status) ? "طلب رسم البطولة غير متاح." : "تعذر تحميل رسم البطولة.", response.status);
  const data = await response.json() as TournamentPayment;
  if (data.id !== id || !Number.isFinite(data.amount) || !data.currency ||
      !["Pending", "Confirmed", "Rejected"].includes(data.status) ||
      (data.status === "Confirmed") !== !!data.receiptNumber ||
      (data.proofAssetId ? !/^[a-f0-9-]{36}$/i.test(data.proofAssetId) || data.proofUrl !== `/api/v1/assets/${data.proofAssetId}` : !!data.proofUrl))
    throw new Error();
  return data;
}

export function TournamentPaymentReview({ id }: { id: string }) {
  return <TournamentPaymentDetails key={id} id={id} />;
}

function TournamentPaymentDetails({ id }: { id: string }) {
  const queueBack = paymentQueueBack(useSearchParams());
  const path = `/api/v1/manage/tournament-payments/${encodeURIComponent(id)}`;
  const [data, setData] = useState<TournamentPayment>();
  const [reason, setReason] = useState("");
  const [actionError, setActionError] = useState("");
  const [error, setError] = useState<{ message: string; retryable: boolean }>();
  const [revision, setRevision] = useState(0);
  const [decision, setDecision] = useState<TournamentDecision>();
  const [busy, setBusy] = useState(false);
  const mounted = useRef(false);
  const writing = useRef(false);

  useEffect(() => {
    mounted.current = true;
    const controller = new AbortController();
    void readTournamentPayment(path, id, controller.signal).then(value => {
      if (!controller.signal.aborted) setData(value);
    }).catch(caught => {
      if (controller.signal.aborted) return;
      setError({
        message: caught instanceof DashboardRequestError ? caught.message : "تعذر تحميل رسم البطولة.",
        retryable: !(caught instanceof DashboardRequestError && [401, 403, 404].includes(caught.status)),
      });
    });
    return () => { mounted.current = false; controller.abort(); };
  }, [path, id, revision]);

  function prepare(accept: boolean) {
    if (!data || data.status !== "Pending" || !data.canReview || busy || (accept && !data.proofAssetId)) return;
    setDecision({ accept, reason: reason.trim() || null, expectedProofAssetId: data.proofAssetId });
  }

  async function review() {
    if (!data || !decision || writing.current) return;
    writing.current = true;
    setBusy(true);
    setActionError("");
    try {
      try {
        await csrfRequest(`${path}/review`, "POST", decision, {}, { announce: false, signal: AbortSignal.timeout(tournamentRequestTimeoutMs) });
      } catch (caught) {
        if (caught instanceof DashboardRequestError && caught.status >= 400 && caught.status < 500 && caught.status !== 408) {
          if (!mounted.current) return;
          if (caught.status === 422) setActionError(caught.message);
          else {
            setData(undefined);
            setError({ message: caught.message, retryable: true });
          }
          return;
        }
      }

      const saved = await readTournamentPayment(path, id).catch(() => null);
      if (!mounted.current) return;
      if (saved && saved.proofAssetId === decision.expectedProofAssetId && saved.tournamentName === data.tournamentName &&
          saved.player === data.player && saved.amount === data.amount && saved.currency === data.currency &&
          (saved.reviewReason || null) === decision.reason &&
          (decision.accept ? saved.status === "Confirmed" && !!saved.receiptNumber : saved.status === "Rejected" && !saved.receiptNumber)) {
        setData(saved);
      } else {
        setData(undefined);
        setError({ message: "تعذر تأكيد القرار. أعد قراءة الطلب قبل المحاولة.", retryable: true });
      }
    } finally {
      writing.current = false;
      if (mounted.current) { setDecision(undefined); setBusy(false); }
    }
  }

  const amount = data && formatMoneyAr(data.amount, data.currency);
  const status = data?.status === "Confirmed" ? "تم التحصيل" : data?.status === "Rejected" ? "مرفوض" : data?.hasProof ? "قيد المراجعة" : "بانتظار الإثبات";
  return <>
    <PageHeader title="رسم البطولة" context="طلبات الدفع" back={queueBack ?? { href: "/dashboard/subscriptions/payments?type=Tournament", label: "العودة إلى طلبات البطولات" }} />
    {error ? <section className="detail-card">
      <ErrorState message={error.message} />
      {error.retryable && <button className="button button-secondary" type="button" onClick={() => { setError(undefined); setReason(""); setRevision(value => value + 1); }}>إعادة قراءة الطلب</button>}
    </section> : !data ? <LoadingState message="جارٍ تحميل رسم البطولة…" /> : <section className="detail-card subscription-period-detail">
      <div className="payment-review-total">
        <div><span>{data.status === "Confirmed" ? "المبلغ المحصّل" : "رسم المشاركة"}</span><strong>{amount}</strong></div>
        <StatusBadge status={data.status} label={status} />
      </div>
      <div className="period-summary">
        <div><span>البطولة</span><b>{data.tournamentName}</b></div>
        <div><span>اللاعب</span><b>{data.player}</b></div>
        {data.receiptNumber && <div><span>رقم الإيصال</span><b><bdi dir="ltr">{data.receiptNumber}</bdi></b></div>}
        {data.reviewReason && <div><span>ملاحظة المراجعة</span><b>{data.reviewReason}</b></div>}
      </div>
      {data.proofUrl && <a className="button button-secondary" href={data.proofUrl} target="_blank" rel="noreferrer">عرض إثبات التحويل</a>}
      {data.hasProof && !data.proofUrl && <p className="compact-notice">عرض الإثبات يتطلب صلاحية قراءة المدفوعات.</p>}
      {data.status === "Pending" && data.canReview && <div className="form-shell payment-proof-form">
        <label>ملاحظة للأسرة (اختياري)<textarea maxLength={500} value={reason} disabled={busy} onChange={event => setReason(event.target.value)} /></label>
        {actionError && <ErrorState message={actionError} />}
        <div className="form-actions">
          <button className="button button-primary" type="button" disabled={busy || !data.proofAssetId} onClick={() => prepare(true)}>قبول وإصدار الإيصال</button>
          <button className="button button-danger" type="button" disabled={busy} onClick={() => prepare(false)}>رفض الطلب</button>
        </div>
      </div>}
    </section>}
    <ConfirmationDialog open={!!decision} title={decision?.accept ? "تأكيد تحصيل رسم البطولة" : "تأكيد رفض الطلب"}
      message={decision?.accept ? `أكد استلام ${amount} لإصدار إيصال البطولة.` : "سيظهر الرفض وملاحظتك لولي الأمر."}
      confirmLabel={decision?.accept ? "تسجيل التحصيل" : "تأكيد الرفض"} tone={decision?.accept ? "primary" : "danger"} busy={busy}
      onConfirm={() => void review()} onCancel={() => setDecision(undefined)} />
  </>;
}
