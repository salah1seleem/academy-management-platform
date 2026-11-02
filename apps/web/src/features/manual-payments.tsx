"use client";
import Link from "next/link";
import { FormEvent, useCallback, useEffect, useRef, useState } from "react";
import { useSearchParams } from "next/navigation";
import { AdminDataTable, EmptyState, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { csrfRequest } from "./dashboard-api";
import { formatDateAr, formatMoneyAr } from "../lib/formatters";
import { useDateFilterDefaults } from "../components/date-filter-defaults";
type Request = { id: string; type: string; player: string; sport: string; branch: string; plan: string; amount: number; currency: string; status: string; hasProof: boolean; createdAtUtc: string };
type Queue = { from?: string; to?: string; pending: number; newEnrollments: number; renewals: number; products: number; items: Request[] };
const names: Record<string, string> = { Enrollment: "اشتراك جديد", Renewal: "تجديد", FamilyRenewal: "تجديد أسرة", Cancelled: "ملغي", Product: "منتج رياضي", Tournament: "رسم بطولة", Pending: "بانتظار المراجعة", Approved: "مقبول", Rejected: "مرفوض" };
export function PaymentRequestQueue() {
  const defaults = useDateFilterDefaults(); const params = useSearchParams(); const [data, setData] = useState<Queue | null>(null); const [error, setError] = useState("");
  const [type, setType] = useState(params.get("type") ?? ""); const [status, setStatus] = useState(params.get("status") ?? "Pending"); const [from, setFrom] = useState(params.get("from") ?? (params.get("allDates") === "true" ? "" : defaults.from)); const [to, setTo] = useState(params.get("to") ?? (params.get("allDates") === "true" ? "" : defaults.to)); const [branchId, setBranchId] = useState(params.get("branchId") ?? ""); const [branches, setBranches] = useState<{ id: string; arabicName: string }[]>([]);
  const load = useCallback(() => { const q = new URLSearchParams(Object.entries({ type, status, from, to, branchId }).filter(([, value]) => value)); if (!from && !to) q.set("allDates", "true"); fetch(`/api/v1/manage/payment-requests?${q}`).then(async r => { if (!r.ok) throw new Error(); const result = await r.json() as Queue; setData(result); setError(""); }).catch(() => setError("تعذر تحميل طلبات الدفع.")); }, [type, status, from, to, branchId]);
  useEffect(() => { void load(); }, [load]);
  useEffect(() => { fetch("/api/v1/manage/structure/options").then(r => r.ok ? r.json() : null).then(x => setBranches(x?.branches ?? [])).catch(() => setBranches([])); }, []);
  return <><PageHeader title="طلبات الدفع" context="مراجعة التحويلات" description="راجع إثبات الدفع والمبلغ قبل القبول. اختيار المجموعة مطلوب للاشتراكات." /><div className="request-counts"><button aria-pressed={type === ""} onClick={() => setType("")}>الكل <b>{data?.pending ?? "—"}</b></button><button aria-pressed={type === "Enrollment"} onClick={() => setType("Enrollment")}>اشتراكات جديدة <b>{data?.newEnrollments ?? "—"}</b></button><button aria-pressed={type === "Renewal"} onClick={() => setType("Renewal")}>تجديد <b>{data?.renewals ?? "—"}</b></button><button aria-pressed={type === "Product"} onClick={() => setType("Product")}>منتجات رياضية <b>{data?.products ?? "—"}</b></button></div><div className="report-filters"><label>من تاريخ<input type="date" value={from} onChange={e => setFrom(e.target.value)} /></label><label>إلى تاريخ<input type="date" value={to} onChange={e => setTo(e.target.value)} /></label><label>الفرع<select value={branchId} onChange={e => setBranchId(e.target.value)}><option value="">كل الفروع</option>{branches.map(b => <option key={b.id} value={b.id}>{b.arabicName}</option>)}</select></label><label>نوع الطلب<select value={type} onChange={e => setType(e.target.value)}><option value="">الكل</option>{["Enrollment", "Renewal", "Product", "Tournament"].map(k => <option key={k} value={k}>{names[k]}</option>)}</select></label><label>الحالة<select value={status} onChange={e => setStatus(e.target.value)}><option value="">الكل</option>{["Pending", "Approved", "Rejected"].map(k => <option key={k} value={k}>{names[k]}</option>)}</select></label><button onClick={() => { setType(""); setStatus(""); setFrom(""); setTo(""); setBranchId(""); setData(null); setError(""); }}>مسح</button></div>{error && <ErrorState message={error} />}{!data && !error ? <LoadingState /> : data && <section className="list-card">{!data.items.length ? <EmptyState message="لا توجد طلبات مطابقة." /> : <AdminDataTable label="طلبات الدفع" rows={data.items} rowKey={x=>`${x.type}-${x.id}`} columns={[
 {key:"player",header:"اللاعب",primary:true,render:x=><><strong>{x.player}</strong><small>{x.plan}</small></>},
 {key:"context",header:"الرياضة والفرع",render:x=><>{x.sport}<small>{x.branch}</small></>},
 {key:"type",header:"نوع الطلب",render:x=>names[x.type]??"طلب"},
 {key:"amount",header:"المبلغ",render:x=>formatMoneyAr(x.amount,x.currency)},
 {key:"status",header:"المراجعة والإثبات",render:x=><>{names[x.status]??"قيد المراجعة"}{x.status==="Pending"&&<small>{x.hasProof?"وصل الإثبات":"لم يصل إثبات"}</small>}</>},
 {key:"date",header:"تاريخ الطلب",render:x=>formatDateAr(x.createdAtUtc)},
 {key:"action",header:"الإجراء",render:x=><Link className="row-primary-action" href={x.type==="FamilyRenewal"?`/dashboard/family-renewals/${x.id}`:x.type==="Tournament"?`/dashboard/tournament-payments/${x.id}`:x.type==="Enrollment"?`/dashboard/enrollment-requests/${x.id}`:x.type==="Product"?`/dashboard/product-sales/${x.id}`:`/dashboard/subscriptions/payments/${x.id}`}>مراجعة</Link>},
 ]}/>}</section>}<TransferSettings /></>;
}
type Settings = { instaPayAddress?: string|null; walletNumber?: string|null; recipientName?: string|null; configured: boolean; isActive?:boolean };
type TransferDraft={instaPayAddress:string;walletNumber:string;recipientName:string;isActive:boolean};
const transferDraft=(s:Settings):TransferDraft=>({instaPayAddress:s.instaPayAddress?.trim()??"",walletNumber:s.walletNumber?.trim()??"",recipientName:s.recipientName?.trim()??"",isActive:s.isActive??s.configured});
async function readTransferSettings(signal?:AbortSignal){const r=await fetch('/api/v1/payment-settings',{cache:'no-store',signal});if(!r.ok)throw Error('تعذر تحميل إعدادات التحويل.');return await r.json() as Settings;}
async function readTransferContext(signal?:AbortSignal){
 const permission=await fetch('/api/v1/my-permissions',{cache:'no-store',signal});
 if(!permission.ok)throw Error('تعذر التحقق من صلاحية إعدادات التحويل.');
 const values=await permission.json();const allowed=Array.isArray(values)&&values.includes('academy.settings.manage');
 return {allowed,settings:allowed?await readTransferSettings(signal):null};
}
export function TransferSettings({ expanded = false }: { expanded?: boolean }) {
 const [settings,setSettings]=useState<Settings|null>(null),[allowed,setAllowed]=useState<boolean|null>(null),[message,setMessage]=useState(""),[error,setError]=useState(""),[busy,setBusy]=useState(false),[pending,setPending]=useState<TransferDraft|null>(null),[revision,setRevision]=useState(0);
 const mounted=useRef(false),locked=useRef(false),intent=useRef<TransferDraft|null>(null);
 const accept=useCallback((next:{allowed:boolean;settings:Settings|null})=>{
  if(!mounted.current)return;
  setAllowed(next.allowed);setSettings(next.settings);setError('');setMessage('');setPending(null);intent.current=null;locked.current=false;setRevision(v=>v+1);
 },[]);
 const load=useCallback(async()=>{
  try{accept(await readTransferContext());}
  catch(e){if(mounted.current)setError(e instanceof Error?e.message:'تعذر تحميل إعدادات التحويل.');}
 },[accept]);
 useEffect(()=>{mounted.current=true;const c=new AbortController();
  readTransferContext(c.signal).then(next=>{if(!c.signal.aborted)accept(next);}).catch(e=>{if(!c.signal.aborted)setError(e instanceof Error?e.message:'تعذر تحميل إعدادات التحويل.');});
  return()=>{mounted.current=false;c.abort();};
 },[accept]);
 async function verify(expected:TransferDraft){
  const next=await readTransferSettings();if(!mounted.current)return;
  const actual=transferDraft(next);
  if((Object.keys(expected) as (keyof TransferDraft)[]).some(k=>actual[k]!==expected[k]))throw Error('لم تتأكد مطابقة إعدادات التحويل المحفوظة. أعد قراءة الإعدادات الحالية قبل تعديلها.');
  setSettings(next);setRevision(v=>v+1);setMessage('تم حفظ حسابات التحويل وتأكيد قراءتها.');setPending(null);intent.current=null;
 }
 async function save(e:FormEvent<HTMLFormElement>){
  e.preventDefault();if(locked.current||!settings)return;
  const f=new FormData(e.currentTarget);const expected:TransferDraft={instaPayAddress:String(f.get('instapay')??'').trim(),walletNumber:String(f.get('wallet')??'').trim(),recipientName:String(f.get('recipient')??'').trim(),isActive:f.get('active')==='on'};
  if(expected.isActive&&!expected.instaPayAddress&&!expected.walletNumber){setError('أدخل حساب إنستا باي أو رقم المحفظة قبل تفعيل التحويل.');return;}
  locked.current=true;intent.current=expected;setPending(expected);setBusy(true);setError('');setMessage('');
  try{await csrfRequest('/api/v1/payment-settings','PUT',expected,{}, {announce:false});await verify(expected);}
  catch(e){if(mounted.current)setError(e instanceof Error?e.message:'تعذر تأكيد حفظ إعدادات التحويل.');}
  finally{if(mounted.current){locked.current=intent.current!==null;setBusy(false);}}
 }
 async function retryRead(){if(busy||!intent.current)return;setBusy(true);setError('');try{await verify(intent.current);}catch(e){if(mounted.current)setError(e instanceof Error?e.message:'تعذر تأكيد إعدادات التحويل.');}finally{if(mounted.current){locked.current=intent.current!==null;setBusy(false);}}}
 if(allowed===false)return null;
 return <details className="detail-card" open={expanded||undefined}><summary>إعدادات الدفع — إنستا باي والمحفظة</summary>{error&&<ErrorState message={error}/>} {!settings?!error?<LoadingState/>:<button onClick={()=>void load()}>إعادة تحميل إعدادات التحويل</button>:<form key={revision} className="form-shell" onSubmit={e=>void save(e)}><fieldset disabled={busy||pending!==null}><legend>وسائل التحويل المتاحة للأسرة</legend><label className="check-inline"><input type="checkbox" name="active" defaultChecked={transferDraft(settings).isActive}/>تفعيل استقبال إثباتات التحويل</label><label>حساب إنستا باي<input name="instapay" dir="ltr" maxLength={120} defaultValue={settings.instaPayAddress??''}/></label><label>رقم المحفظة<input name="wallet" dir="ltr" maxLength={30} defaultValue={settings.walletNumber??''}/></label><label>اسم المستلم<input name="recipient" maxLength={120} defaultValue={settings.recipientName??''}/></label><button className="primary-button">{busy?'جارٍ الحفظ…':'حفظ'}</button></fieldset>{pending&&!busy&&<div className="form-actions"><button type="button" onClick={()=>void retryRead()}>إعادة التحقق من الحفظ</button><button type="button" onClick={()=>void load()}>إعادة قراءة الإعدادات الحالية</button></div>}{message&&<p role="status">{message}</p>}</form>}</details>;
}
export function ManualPaymentReview({ id, branchId, sportId, groupId, onReviewed }: { id: string; branchId?: string; sportId?: string; groupId?: string; onReviewed?: () => Promise<unknown> }) {
  const [proof, setProof] = useState<{ url: string; status: string; reviewReason?: string } | null>(null); const [groups, setGroups] = useState<{ id: string; arabicName: string; branchId: string; sportId: string }[]>([]); const [selected, setSelected] = useState(groupId ?? ""); const [reason, setReason] = useState(""); const [message, setMessage] = useState(""); const [busy, setBusy] = useState(false);
  useEffect(() => { fetch(`/api/v1/manage/payments/${id}/proof`).then(r => r.ok ? r.json() : null).then(setProof); fetch("/api/v1/manage/structure/options").then(r => r.ok ? r.json() : { groups: [] }).then(x => setGroups(x.groups)); }, [id]);
  async function review(accept: boolean) { if (busy) return; setBusy(true); try { await csrfRequest(`/api/v1/manage/payments/${id}/review`, "POST", { accept, groupId: selected || null, reason }); setProof(p => p && { ...p, status: accept ? "Accepted" : "Rejected" }); await onReviewed?.(); setMessage(accept ? "تم قبول الدفع وتفعيل الاشتراك. حدّث الصفحة لعرض الإيصال." : "تم رفض الطلب وإبلاغ ولي الأمر."); } catch (e) { setMessage(e instanceof Error ? e.message : "تعذر مراجعة الطلب."); } finally { setBusy(false); } }
  return proof && <section className="detail-card"><h2>إثبات التحويل</h2><a href={proof.url} target="_blank" rel="noreferrer">فتح الإيصال المرفوع</a>{proof.status === "Pending" ? <><label>إسناد المجموعة<select value={selected} onChange={e => setSelected(e.target.value)}><option value="">اختر المجموعة</option>{groups.filter(g => g.branchId === branchId && g.sportId === sportId).map(g => <option key={g.id} value={g.id}>{g.arabicName}</option>)}</select></label><label>سبب الرفض أو ملاحظة (اختياري)<textarea maxLength={500} value={reason} onChange={e => setReason(e.target.value)} /></label><div className="form-actions"><button className="primary-button" disabled={busy || !selected} onClick={() => void review(true)}>قبول وتفعيل الاشتراك</button><button className="danger-button" disabled={busy} onClick={() => void review(false)}>رفض</button></div></> : <p>{proof.status === "Accepted" ? "تم قبول الإثبات" : "تم رفض الإثبات"} · {proof.reviewReason}</p>}{message && <p role="status">{message}</p>}</section>;
}
export function ProductPaymentReview({ id }: { id: string }) {
  const [data, setData] = useState<{ productName: string; sizeSnapshot?:string;shirtNumberSnapshot?:number|null;quantity: number; amount: number; currency: string; status: string; proofUrl?: string } | null>(null); const [reason, setReason] = useState(""); const [error, setError] = useState(""); const [busy, setBusy] = useState(false);
  const load = useCallback(() => fetch(`/api/v1/manage/product-sales/${id}`).then(r => r.ok ? r.json() : Promise.reject()).then(setData).catch(() => setError("تعذر تحميل طلب المنتج.")), [id]); useEffect(() => { void load(); }, [load]);
  async function review(accept: boolean) { setBusy(true); try { await csrfRequest(`/api/v1/manage/product-sales/${id}/review`, "POST", { accept, reason }); await load(); } catch (e) { setError(e instanceof Error ? e.message : "تعذر مراجعة الطلب."); } finally { setBusy(false); } }
  return <><PageHeader title="طلب منتج رياضي" context="طلبات الدفع" />{error && <ErrorState message={error} />}{data && <section className="detail-card"><h2>{data.productName}</h2>{data.sizeSnapshot&&<p>المقاس {data.sizeSnapshot}{data.shirtNumberSnapshot!=null?` · رقم القميص ${data.shirtNumberSnapshot}`:""}</p>}<p>الكمية {data.quantity} · {formatMoneyAr(data.amount, data.currency)}</p>{data.proofUrl && <a href={data.proofUrl} target="_blank" rel="noreferrer">فتح إثبات الدفع</a>}{data.status === "Pending" ? <><label>ملاحظة (اختياري)<textarea value={reason} maxLength={500} onChange={e => setReason(e.target.value)} /></label><div className="form-actions"><button disabled={busy || !data.proofUrl} onClick={() => void review(true)}>قبول وخصم المخزون</button><button disabled={busy} onClick={() => void review(false)}>رفض</button></div></> : <p>{names[data.status] ?? (data.status === "Confirmed" ? "تم التحصيل" : data.status)}</p>}</section>}</>;
}
