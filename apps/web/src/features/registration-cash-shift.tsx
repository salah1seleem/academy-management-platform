"use client";
import Link from "next/link";
import { useEffect, useState } from "react";
import { ErrorState, LoadingState } from "../components/dashboard-shell";

type Shift = { id: string; name: string; branch: string };
type Props = {
  branchId: string; planId: string; value: string; onChange: (value: string) => void; disabled?: boolean;
};
export function RegistrationCashShift(props: Props) {
  const [refresh, setRefresh] = useState(0);
  return <CashShiftOptions key={`${props.branchId}:${props.planId}:${refresh}`} {...props} onRefresh={() => { props.onChange(""); setRefresh(n => n + 1); }} />;
}
function CashShiftOptions({ branchId, planId, value, onChange, disabled = false, onRefresh }: Props & { onRefresh: () => void }) {
  const [items, setItems] = useState<Shift[] | null>(null), [error, setError] = useState("");
  useEffect(() => {
    let active = true;
    if (!branchId || !planId) return;
    const query = new URLSearchParams({ branchId, planId });
    fetch(`/api/v1/people/registration-cash-options?${query}`, { cache: "no-store" }).then(async r => {
      const body = await r.json(); if (!r.ok) throw new Error(body.title ?? "تعذر تحميل وردياتك المخولة.");
      return body.sessions as Shift[];
    }).then(rows => { if (active) { setItems(rows); onChange(""); } }).catch(e => { if (active) { onChange(""); setError(e instanceof Error && /[\u0600-\u06ff]/.test(e.message) ? e.message : "تعذر تحميل الورديات. تحقق من الاتصال ثم اضغط تحديث الورديات."); } });
    return () => { active = false; };
  }, [branchId, planId, onChange]);
  return <section aria-label="وردية قبض التسجيل" className="adjustment-preview registration-cash-options">
    <h3>التحصيل من ورديتك</h3>
    {!branchId || !planId ? <p>اختر الفرع والباقة لعرض وردياتك المتاحة.</p> : <>
      {error && <ErrorState message={error} />}
      {!items && !error && <LoadingState />}
      {items && <label>وردية التحصيل<select required value={value} disabled={disabled} onChange={e => onChange(e.target.value)}>
        <option value="">اختر وردية مفتوحة مخولة</option>{items.map(i => <option key={i.id} value={i.id}>{i.name} · {i.branch}</option>)}
      </select></label>}
      {items?.length === 0 && <p>لا توجد وردية مفتوحة لك في هذا الفرع وبعملة الباقة.</p>}
      <div className="form-actions"><Link className="secondary-button" href="/dashboard/finance/cash-registers">فتح الصناديق والورديات</Link>
        <button className="secondary-button" type="button" disabled={disabled} onClick={onRefresh}>تحديث الورديات</button></div>
    </>}
  </section>;
}
