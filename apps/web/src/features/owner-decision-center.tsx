"use client";

import Link from "next/link";
import {useSearchParams} from "next/navigation";
import {useEffect, useRef, useState} from "react";
import {ApprovedWorkspace} from "../components/approved-workspace";
import {AdminDataTable, EmptyState, ErrorState, LoadingState, PageHeader} from "../components/dashboard-shell";
import {csrfRequest, DashboardRequestError} from "./dashboard-api";
import {GroupPlacement} from "./group-placement";
import {
  decisionApi, decisionPath, decisionQuery, ownerDecisionRecords, smartDecisionLabels, smartDecisionRecords,
  type DecisionKind, type DecisionSources, type HomeDecision, type SmartDecisionCounts,
} from "./owner-home-decisions";

type Summary = {
  from: string; to: string;
  operations: Pick<DecisionSources,"groupAttention"|"underAssignedCoaches"> & {pendingPaymentQueueBacklog: number};
};
type Player = {playerId: string; enrollmentId: string; name: string; branchId: string; branch: string; sportId: string; sport: string; taskId?: string | null};
type Players = {items: Player[]; total: number};
type Group = {groupId: string; name: string; branchId: string; branch: string; sportId: string; sport: string; capacity: number; occupied: number; available: number};
type Groups = {today: string; groups: Group[]};

async function read<T>(path: string, signal: AbortSignal): Promise<T> {
  const message = "تعذر تحميل القرارات.";
  let response: Response;
  try {
    response = await fetch(path,{signal,cache:"no-store"});
  } catch {
    throw new Error(message);
  }
  if (response.status === 403) throw new DashboardRequestError("حسابك لا يملك صلاحية عرض قرارات المالك في هذا النطاق.", 403);
  if (!response.ok) throw new DashboardRequestError(message, response.status);
  return response.json().catch(() => { throw new Error(message); }) as Promise<T>;
}
const denied = (error: unknown) => error instanceof DashboardRequestError && error.status === 403;
function errorMessage(error: unknown) {
  return error instanceof Error && /\p{Script=Arabic}/u.test(error.message) ? error.message : "تعذر إكمال الإجراء.";
}

export function OwnerDecisionCenter() {
  const incoming = useSearchParams();
  const branch = incoming.get("branchId") ?? "";
  const requested = incoming.get("kind");
  const kind = requested && Object.hasOwn(smartDecisionLabels,requested) ? requested as DecisionKind : null;
  const all = `${decisionPath}?${decisionQuery(branch)}`;

  return <ApprovedWorkspace>
    <PageHeader
      title={kind ? smartDecisionLabels[kind] : "مركز القرارات الذكية"}
      context="الأكاديمية"
      secondaryActions={kind ? [{label:"العودة إلى كل القرارات",href:all}] : []}
    />
    {kind === "Capacity"
      ? <CapacityActions key={branch} branch={branch} candidate={incoming.get("candidateId")} sportId={incoming.get("sportId")}/>
      : kind ? <PlayerActions key={`${branch}:${kind}`} branch={branch} kind={kind}/>
      : <AllDecisions key={branch} branch={branch} query={incoming.toString()}/>}
  </ApprovedWorkspace>;
}

function AllDecisions({branch,query}: {branch:string;query:string}) {
  const [result,setResult] = useState<{query:string;revision:number;records?:HomeDecision[];error?:string;denied?:boolean}>();
  const [revision,setRevision] = useState(0);
  useEffect(() => {
    const request = new AbortController();
    const signal = request.signal;
    Promise.all([
      read<SmartDecisionCounts>(`${decisionApi}?${decisionQuery(branch)}`,signal),
      read<DecisionSources>(`/api/v1/reports/owner-cockpit?${query}`,signal),
      read<Summary>(`/api/v1/reports/owner-summary?${query}&includeSubscriptionAnalytics=false`,signal),
    ]).then(([decisions,cockpit,summary]) => {
      if (signal.aborted) return;
      setResult({query,revision,records:[
        ...smartDecisionRecords(decisions,branch,summary.operations.pendingPaymentQueueBacklog),
        ...ownerDecisionRecords(
          {...cockpit,...summary.operations,risk:{players:0},capacity:{seats:0}},
          {from:summary.from,to:summary.to,branchId:branch},
        ),
      ]});
    }).catch(caught => {if (!signal.aborted) setResult({query,revision,error:errorMessage(caught),denied:denied(caught)});});
    return () => request.abort();
  }, [branch,query,revision]);
  const current = result?.query === query && result.revision === revision ? result : undefined;
  const records = current?.records;
  if (current?.error) return <><ErrorState message={current.error}/>{!current.denied && <button type="button" className="button button-secondary" onClick={()=>setRevision(value=>value+1)}>إعادة تحميل القرارات</button>}</>;
  if (!records) return <LoadingState/>;
  if (!records.length) return <EmptyState message="لا توجد حالات تحتاج متابعة في النطاق الحالي." />;
  return <div className="decision-grid">{records.map((record,index) => <section className="detail-card" key={`${index}:${record.href}`}>
    <h2>{record.title}</h2><p>{record.line}</p><Link className="button button-secondary" href={record.href}>{record.action}<span aria-hidden="true"> ←</span></Link>
  </section>)}</div>;
}

function PlayerActions({branch,kind}: {branch:string;kind:Exclude<DecisionKind,"Capacity">}) {
  const [page,setPage] = useState(1);
  const [revision,setRevision] = useState(0);
  const [result,setResult] = useState<{page:number;revision:number;data?:Players;error?:string;denied?:boolean}>();
  useEffect(() => {
    const request = new AbortController();
    read<Players>(`${decisionApi}?${decisionQuery(branch,kind,page)}`,request.signal)
      .then(data => {if (!request.signal.aborted) setResult({page,revision,data});})
      .catch(caught => {if (!request.signal.aborted) setResult({page,revision,error:errorMessage(caught),denied:denied(caught)});});
    return () => request.abort();
  }, [branch,kind,page,revision]);
  const current = result?.page === page && result.revision === revision ? result : undefined;
  const data = current?.data;
  const solution = {
    Expiring:"ذكّر الأسر قبل انتهاء الاشتراك، أو أسند التواصل لموظف مختص.",
    Expired:"ادعُ الأسر للعودة للتدريب، أو أسند متابعة التجديد لموظف مختص.",
    Absence:"تابع سبب الغياب مع الأسرة. رابط الشكاوى والاقتراحات يصل إلى المالك فقط.",
    Unassigned:"اختر مجموعة من نفس الفرع والرياضة، ثم راجع السعة والأهلية قبل التسكين.",
  }[kind];
  return <section className="detail-card">
    <p>{solution}</p>
    {kind!=="Unassigned" && data && data.total>0 && <Link className="button button-primary"
      href={`/dashboard/notifications/reminders?${new URLSearchParams({kind,branchId:branch})}`}>معاينة تنبيه للأسر</Link>}
    {current?.error ? <><ErrorState message={current.error}/>{!current.denied && <button type="button" className="button button-secondary" onClick={()=>setRevision(value=>value+1)}>إعادة المحاولة</button>}</> : !data ? <LoadingState/> : data.total === 0 ? <EmptyState message="لا يوجد لاعبون في هذه الحالة."/> : <>
      <h2>{data.total} لاعب</h2>
      <AdminDataTable label={smartDecisionLabels[kind]} rows={data.items} rowKey={player=>player.enrollmentId} paginate={false} columns={[
        {key:"name",header:"اللاعب",primary:true,render:player=><strong>{player.name}</strong>},
        {key:"scope",header:"الفرع والرياضة",render:player=><>{player.branch} · {player.sport}</>},
        {key:"action",header:"الإجراء",render:player => player.taskId ?
          <Link className="row-primary-action" href={`/dashboard/operations/follow-ups/${encodeURIComponent(player.taskId)}`}>عرض المتابعة</Link> : kind==="Unassigned" ?
          <Link className="row-primary-action" href={`${decisionPath}?${new URLSearchParams({kind:"Capacity",branchId:player.branchId,sportId:player.sportId,candidateId:player.enrollmentId})}`}>اختيار مجموعة</Link> :
          <Link className="row-primary-action" href={`/dashboard/operations/follow-ups/new?${new URLSearchParams({source:"record",sourceId:player.playerId,branchId:player.branchId,sportId:player.sportId,decision:kind})}`}>إسناد المتابعة</Link>},
      ]}/>
      {(page>1 || data.total>50) && <nav className="row-actions" aria-label="صفحات اللاعبين">
        <button type="button" className="button button-secondary" disabled={page===1} onClick={()=>setPage(value=>value-1)}>السابق</button>
        <span>صفحة {page}</span>
        <button type="button" className="button button-secondary" disabled={page*50>=data.total} onClick={()=>setPage(value=>value+1)}>التالي</button>
      </nav>}
    </>}
  </section>;
}

type MergePreview = {fingerprint:string;count:number;source:string;target:string;conflicts:string[]};
function CapacityActions({branch,candidate,sportId}: {branch:string;candidate:string|null;sportId:string|null}) {
  const [mode,setMode] = useState<"placement"|"merge">("placement");
  const [source,setSource] = useState("");
  const [target,setTarget] = useState("");
  const [data,setData] = useState<Groups>();
  const [preview,setPreview] = useState<MergePreview>();
  const [status,setStatus] = useState("");
  const [error,setError] = useState("");
  const [busy,setBusy] = useState(false);
  const [uncertain,setUncertain] = useState(false);
  const [revision,setRevision] = useState(0);
  const requestId = useRef("");
  useEffect(() => {
    const request = new AbortController();
    read<Groups>(`${decisionApi}?${decisionQuery(branch,"Capacity")}`,request.signal)
      .then(value => {if (!request.signal.aborted) setData(value);})
      .catch(caught => {if (!request.signal.aborted) setError(errorMessage(caught));});
    return () => request.abort();
  }, [branch,revision]);
  const from = data?.groups.find(group=>group.groupId===source);
  const targets = data?.groups.filter(group => mode==="merge"
    ? from && group.groupId!==from.groupId && group.branchId===from.branchId && group.sportId===from.sportId && group.available>=from.occupied
    : group.available>0 && (!sportId || group.sportId===sportId)) ?? [];
  const to = targets.find(group=>group.groupId===target);
  function changeMode(next: typeof mode) {
    setMode(next); setTarget(""); setSource(""); setPreview(undefined); setError(""); setStatus("");
  }
  async function merge(save: boolean) {
    if (!data || !from || !to || busy || !save && uncertain || save && (!preview || preview.conflicts.length)) return;
    if (!save) requestId.current = crypto.randomUUID();
    setBusy(true); setError(""); setStatus("");
    try {
      const response = await csrfRequest(`/api/v1/manage/groups/merge${save?"":"/preview"}`,"POST",
        {sourceGroupId:from.groupId,targetGroupId:to.groupId,effectiveDate:data.today,fingerprint:save?preview?.fingerprint:undefined},
        save?{"Idempotency-Key":requestId.current}:{},{announce:false});
      if (!save) setPreview(await response.json() as MergePreview);
      else {
        setUncertain(false); setPreview(undefined); setSource(""); setTarget(""); setRevision(value=>value+1);
        setStatus("تم نقل اللاعبين إلى المجموعة المستقبلة.");
      }
    } catch (caught) {
      const unknown = save && (!(caught instanceof DashboardRequestError) || caught.status >= 500 || caught.status === 408 || caught.status === 499);
      setUncertain(unknown);
      setError(unknown ? "لم يصل تأكيد الدمج. أعد تأكيد الطلب نفسه للتحقق من نتيجته." : errorMessage(caught));
    }
    finally {setBusy(false);}
  }
  if (!data) return error ? <ErrorState message={error}/> : <LoadingState/>;
  return <section className="detail-card">
    <div className="row-actions">
      <button type="button" className="secondary-button" aria-pressed={mode==="placement"} disabled={busy || uncertain}
        onClick={()=>changeMode("placement")}>تسكين لاعبين</button>
      <button type="button" className="secondary-button" aria-pressed={mode==="merge"} disabled={busy || uncertain}
        onClick={()=>changeMode("merge")}>دمج مجموعات</button>
      {!candidate && <Link href={`${decisionPath}?${decisionQuery(branch,"Unassigned")}`}>اللاعبون بانتظار التسكين</Link>}
    </div>
    {error && <ErrorState message={error}/>}
    {status && <p role="status">{status}</p>}
    <div className="form-shell">
      {mode==="merge" && <label>المجموعة المنقول منها<select disabled={busy || uncertain} value={source}
        onChange={event=>{setSource(event.target.value);setTarget("");setPreview(undefined);}}>
        <option value="">اختر المجموعة</option>
        {data.groups.filter(group=>group.occupied>0).map(group=><option key={group.groupId} value={group.groupId}>
          {group.name} · {group.branch} · {group.occupied} لاعب
        </option>)}
      </select></label>}
      <label>{mode==="merge"?"المجموعة المستقبلة":"اختر مجموعة للتسكين"}<select disabled={busy || uncertain} value={target}
        onChange={event=>{setTarget(event.target.value);setPreview(undefined);}}>
        <option value="">اختر المجموعة</option>
        {targets.map(group=><option key={group.groupId} value={group.groupId}>
          {group.name} · {group.branch} · {group.available} مكان
        </option>)}
      </select></label>
      {mode==="placement" && to && <GroupPlacement key={to.groupId} id={to.groupId} sportId={to.sportId} focusEnrollmentId={candidate}
        onSaved={()=>setRevision(value=>value+1)}/>}
      {mode==="merge" && <>
        <button type="button" className="secondary-button" disabled={!from||!to||busy||uncertain} onClick={()=>void merge(false)}>معاينة الدمج</button>
        {preview && <section aria-label="معاينة الدمج">
          <p>نقل {preview.count} لاعب من {preview.source} إلى {preview.target}.</p>
          {preview.conflicts.map(conflict=><p key={conflict} role="alert">{conflict}</p>)}
          <button type="button" className="primary-button" disabled={busy||preview.conflicts.length>0} onClick={()=>void merge(true)}>{busy ? "جارٍ التأكيد…" : uncertain ? "إعادة تأكيد نفس الدمج" : "تأكيد الدمج"}</button>
        </section>}
      </>}
    </div>
  </section>;
}
