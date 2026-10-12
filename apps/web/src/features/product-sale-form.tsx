"use client";
import Link from "next/link";
import { FormEvent, useEffect, useRef, useState } from "react";
import { ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { csrfRequest } from "./dashboard-api";
import { formatMoneyAr } from "../lib/formatters";
type Product = { arabicName: string; stockQuantity: number; isActive: boolean };
type Item = { id: string; arabicName: string };
type Quote = { unitPrice: string; amount: string; currency: string; stockQuantity: number; cashEnabled: boolean; sessions: { id: string; name: string }[] };
export function ProductSaleForm({ id }: { id: string }) {
  const [product, setProduct] = useState<Product | null>(null), [branches, setBranches] = useState<Item[]>([]), [players, setPlayers] = useState<Item[]>([]), [error, setError] = useState("");
  useEffect(() => {
    let active = true;
    Promise.all([fetch(`/api/v1/content/catalog/${id}`).then(async r => { if (!r.ok) throw new Error(); return r.json(); }), fetch("/api/v1/manage/product-sales/options").then(async r => { if (!r.ok) throw new Error(); return r.json(); }), fetch("/api/v1/people/players?search=").then(r => r.ok ? r.json() : [])])
      .then(([p, o, list]) => { if (active) { setProduct(p); setBranches(o?.branches ?? []); setPlayers(list); } }).catch(() => { if (active) setError("تعذر تحميل بيانات بيع المنتج."); });
    return () => { active = false; };
  }, [id]);
  return <><PageHeader title="بيع منتج رياضي" context="المنتجات الرياضية / تحصيل" />{error && <ErrorState message={error} />}{!product ? !error && <LoadingState /> : <ProductSaleEditor key={id} id={id} product={product} branches={branches} players={players} />}</>;
}
function ProductSaleEditor({ id, product, branches, players }: { id: string; product: Product; branches: Item[]; players: Item[] }) {
  const [branch, setBranch] = useState(""), [player, setPlayer] = useState(""), [quantity, setQuantity] = useState(1), [method, setMethod] = useState("");
  const [refresh, setRefresh] = useState(0), [locked, setLocked] = useState(false), key = useRef("");
  return <section className="detail-card"><h2>{product.arabicName}</h2><p>المخزون المتاح {product.stockQuantity}</p>
    <div className="form-shell"><label>الفرع<select disabled={locked} value={branch} onChange={e => setBranch(e.target.value)}><option value="">اختر الفرع</option>{branches.map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select></label>
      <label>اللاعب — اختياري<select disabled={locked} value={player} onChange={e => setPlayer(e.target.value)}><option value="">بيع مباشر دون لاعب</option>{players.map(x => <option key={x.id} value={x.id}>{x.arabicName}</option>)}</select></label>
      <label>الكمية<input disabled={locked} type="number" min="1" max={Math.min(100, product.stockQuantity)} step="1" value={quantity} onChange={e => setQuantity(Number(e.target.value))} /></label>
      <label>طريقة الدفع<select disabled={locked} value={method} onChange={e => setMethod(e.target.value)}><option value="">اختر طريقة الدفع</option><option value="Cash">نقدي من وردية مخولة</option><option value="Online">تحويل إلكتروني تم استلامه</option></select></label></div>
    {!product.isActive ? <p>المنتج متوقف؛ لا يمكن تسجيل بيع جديد.</p> : !branch || !method || !Number.isInteger(quantity) || quantity < 1 ? <p>اختر الفرع والكمية وطريقة الدفع لمراجعة المبلغ.</p> :
      <ProductSaleConfirmation key={`${branch}:${player}:${quantity}:${method}:${refresh}`} productId={id} branchId={branch} playerId={player} quantity={quantity} method={method} requestKey={key} onLocked={setLocked} onRefresh={() => setRefresh(n => n + 1)} />}
  </section>;
}
function ProductSaleConfirmation({ productId, branchId, playerId, quantity, method, requestKey, onRefresh, onLocked }: { productId: string; branchId: string; playerId: string; quantity: number; method: string; requestKey: { current: string }; onRefresh: () => void; onLocked: (value: boolean) => void }) {
  const [quote, setQuote] = useState<Quote | null>(null), [shift, setShift] = useState(""), [error, setError] = useState(""), [busy, setBusy] = useState(false), [result, setResult] = useState<{ id: string; amount: string; currency: string } | null>(null);
  useEffect(() => {
    let active = true; const query = new URLSearchParams({ productId, branchId, quantity: String(quantity) }); if (playerId) query.set("playerId", playerId);
    fetch(`/api/v1/manage/product-sales/quote?${query}`, { cache: "no-store" }).then(async r => { const d = await r.json(); if (!r.ok) throw new Error(d.title ?? "تعذر مراجعة مبلغ البيع."); return d as Quote; }).then(d => { if (active) setQuote(d); }).catch(e => { if (active) setError(e instanceof Error && /[\u0600-\u06ff]/.test(e.message) ? e.message : "تعذر تحميل المراجعة. تحقق من الاتصال ثم حدّث المبلغ والورديات."); });
    return () => { active = false; };
  }, [productId, branchId, playerId, quantity]);
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); if (!quote || busy || method === "Cash" && !shift) return;
    if (!window.confirm(`تأكيد استلام ${formatMoneyAr(quote.amount, quote.currency)} وتسليم المنتج؟ سيُخصم المخزون مرة واحدة.`)) return;
    setBusy(true); onLocked(true); setError(""); requestKey.current ||= crypto.randomUUID();
    try { const response = await csrfRequest("/api/v1/manage/product-sales", "POST", { productId, branchId, playerId: playerId || null, quantity, paymentMethod: method, expectedAmount: quote.amount, cashSessionId: method === "Cash" ? shift : null }, { "Idempotency-Key": requestKey.current }); setResult(await response.json()); }
    catch (e) { onLocked(false); setError(e instanceof Error && /[\u0600-\u06ff]/.test(e.message) ? e.message : "تعذر تسجيل البيع. احتفظ بالطلب وأعد المحاولة؛ لن يتكرر التحصيل."); }
    finally { setBusy(false); }
  }
  if (result) return <section className="detail-card"><h2>تم التحصيل وخصم المخزون</h2><p>{formatMoneyAr(result.amount, result.currency)}</p><div className="form-actions"><Link className="primary-button" href={`/dashboard/product-sales/${result.id}`}>تفاصيل العملية</Link><Link className="secondary-button" href="/dashboard/content/catalog">العودة إلى المنتجات</Link></div></section>;
  return <section aria-label="مراجعة تحصيل المنتج">{error && <ErrorState message={error} />}{!quote && !error && <LoadingState />}
    {quote && <form className="form-shell" onSubmit={e => void submit(e)}><strong>الإجمالي {formatMoneyAr(quote.amount, quote.currency)}</strong><p>سعر القطعة {formatMoneyAr(quote.unitPrice, quote.currency)} · الكمية {quantity}</p>
      {method === "Cash" && (quote.cashEnabled ? <label>وردية التحصيل<select required value={shift} disabled={busy} onChange={e => setShift(e.target.value)}><option value="">اختر ورديتك المفتوحة</option>{quote.sessions.map(s => <option key={s.id} value={s.id}>{s.name}</option>)}</select>{!quote.sessions.length && <small>لا توجد وردية مفتوحة مخولة في فرع البيع وعملة المنتج.</small>}</label> : <p>النقدي غير مفعل لهذه الأكاديمية.</p>)}
      <small>المراجعة من الخادم؛ تغير السعر أو نقص المخزون يوقف التحصيل. البيع لا يغير اشتراك اللاعب.</small><div className="form-actions"><button className="primary-button" disabled={busy || method === "Cash" && (!quote.cashEnabled || !shift)}>تأكيد التحصيل والبيع</button></div></form>}
    <div className="form-actions"><button type="button" disabled={busy} onClick={onRefresh}>تحديث المبلغ والورديات</button><Link className="secondary-button" href="/dashboard/finance/cash-registers">فتح الصناديق والورديات</Link></div>
  </section>;
}
