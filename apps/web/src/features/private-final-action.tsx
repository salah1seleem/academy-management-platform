"use client";
import {useRef,useState} from "react";
import {csrfRequest,DashboardRequestError} from "./dashboard-api";
import {formatDateAr,formatMoneyAr} from "../lib/formatters";
type Quote={quoteHash:string;canFinalize:boolean;contractEnd:string|null;difference:number|null;currency:string;message:string};
export function PrivateFinalAction({path,onChanged}:{path:string;onChanged:()=>Promise<void>}){
  const [quote,setQuote]=useState<Quote|null>(null),[busy,setBusy]=useState(false),[note,setNote]=useState(""),[error,setError]=useState("");
  const keys=useRef(new Map<string,string>());
  async function preview(){setBusy(true);setError("");setQuote(null);try{const r=await fetch(`${path}/final-preview`,{cache:"no-store"});if(!r.ok)throw new Error();setQuote(await r.json());}catch{setError("تعذر مراجعة مستحق نهاية العقد. تحقق من الصلاحية والاتصال.");}finally{setBusy(false);}}
  async function confirm(){if(!quote?.canFinalize||busy)return;setBusy(true);setError("");const body={quoteHash:quote.quoteHash,note:note.trim()||null},signature=JSON.stringify(body);if(!keys.current.has(signature))keys.current.set(signature,crypto.randomUUID());try{await csrfRequest(`${path}/final-settlement`,"POST",body,{"Idempotency-Key":keys.current.get(signature)!});await onChanged();}catch(e){setError(e instanceof DashboardRequestError?e.message:"تعذر تأكيد الحفظ؛ أعد المراجعة دون إنشاء طلب مكرر.");}finally{setBusy(false);}}
  return <section className="detail-card private-correction-form" aria-label="عمولة بعد نهاية العقد"><h3>مستحق عمولة بعد نهاية العقد</h3><p>للعمولة الموجبة فقط بعد انتهاء مدة العمل. لا ننشئ راتب شهر جديد أو نصرف مالًا عند الاعتماد.</p><button className="secondary-button" disabled={busy} onClick={()=>void preview()}>معاينة مستحق نهاية العقد</button>
    {quote&&<><p>{quote.message}</p>{quote.contractEnd&&<p>انتهاء العقد: {formatDateAr(quote.contractEnd)}</p>}{quote.difference!==null&&<strong>الفرق المحسوب: {formatMoneyAr(quote.difference,quote.currency)}</strong>}{quote.canFinalize&&<><label>ملاحظة — اختياري<textarea maxLength={500} value={note} disabled={busy} onChange={e=>setNote(e.target.value)}/></label><button className="primary-button" disabled={busy} onClick={()=>void confirm()}>تأكيد اعتماد العمولة دون صرف</button></>}</>}{error&&<p role="alert">{error}</p>}
  </section>;
}
