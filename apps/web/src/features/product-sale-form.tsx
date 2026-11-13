"use client";

import Link from "next/link";
import { type FormEvent, useEffect, useRef, useState } from "react";
import { ConfirmationDialog, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { apiProblemMessage } from "../lib/api-problem";
import { formatMoneyAr } from "../lib/formatters";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";

const sales = "/api/v1/manage/product-sales";
type Variant = { id: string; size: string; color?: string; shirtNumber: number | null; quantity: number };
type Product = { arabicName: string; stockQuantity: number; isActive: boolean; usesVariants?: boolean; variants?: Variant[] };
type Item = { id: string; arabicName: string };
type Quote = { unitPrice: string; amount: string; currency: string; stockQuantity: number; cashEnabled: boolean; sessions: { id: string; name: string }[] };
type Method = "" | "Cash" | "Online";
type Selection = { productId: string; branchId: string; playerId: string | null; quantity: number; variantId?: string };
type Command = { key: string; currency: string; body: Selection & { paymentMethod: Exclude<Method, "">; expectedAmount: string; cashSessionId: string | null } };
type SavedSale = { id: string; amount: string; currency: string };

function variantLabel(variant: Variant) {
  return [variant.color?.trim(), variant.size, variant.shirtNumber == null ? null : `رقم ${variant.shirtNumber}`].filter(Boolean).join(" · ");
}

async function readJson(response: Response, fallback: string) {
  if (!response.ok) throw new DashboardRequestError(await apiProblemMessage(response, fallback), response.status);
  return response.json();
}

export function ProductSaleForm({ id }: { id: string }) {
  const [state, setState] = useState<{ id: string; product: Product; branches: Item[]; players: Item[] } | null>(null);
  const [error, setError] = useState("");
  const [revision, setRevision] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    const init = { cache: "no-store", signal: controller.signal } as const;
    async function readProduct() {
      const product = await readJson(await fetch(`/api/v1/content/catalog/${id}`, init), "تعذر تحميل المنتج.");
      if (!product.usesVariants) return product as Product;
      const inventory = await readJson(await fetch(`/api/v1/content/catalog/${id}/inventory`, init), "تعذر تحميل مخزون المقاسات.");
      return { ...product, stockQuantity: inventory.stockQuantity ?? product.stockQuantity, variants: inventory.variants } as Product;
    }

    void Promise.all([
      readProduct(),
      fetch(`${sales}/options`, init).then(response => readJson(response, "خيارات البيع غير متاحة لهذا الحساب.")),
      fetch("/api/v1/people/players?search=", init).then(response => response.ok ? response.json() : []).catch(() => []),
    ]).then(([product, options, players]) => {
      if (!controller.signal.aborted) {
        setState({ id, product, branches: options.branches ?? [], players: Array.isArray(players) ? players : players?.items ?? [] });
        setError("");
      }
    }).catch(cause => {
      if (!controller.signal.aborted) {
        setState(null);
        setError(cause instanceof DashboardRequestError ? cause.message : "تعذر تحميل بيانات بيع المنتج.");
      }
    });
    return () => controller.abort();
  }, [id, revision]);

  return <>
    <PageHeader title="بيع منتج رياضي" context="المنتجات الرياضية" back={{ label: "العودة إلى المنتج", href: `/dashboard/content/catalog/${id}` }} />
    {error && <><ErrorState message={error} /><button className="secondary-button" onClick={() => setRevision(value => value + 1)}>إعادة تحميل البيع</button></>}
    {state?.id === id
      ? <ProductSaleEditor key={id} id={id} product={state.product} branches={state.branches} players={state.players} reload={() => setRevision(value => value + 1)} />
      : !error && <LoadingState />}
  </>;
}

function ProductSaleEditor({ id, product, branches, players, reload }: { id: string; product: Product; branches: Item[]; players: Item[]; reload: () => void }) {
  const [variant, setVariant] = useState("");
  const [branch, setBranch] = useState("");
  const [player, setPlayer] = useState("");
  const [quantityInput, setQuantityInput] = useState("1");
  const [method, setMethod] = useState<Method>("");
  const [refresh, setRefresh] = useState(0);
  const [locked, setLocked] = useState(false);
  const selectedVariant = product.variants?.find(value => value.id === variant);
  const available = product.usesVariants ? selectedVariant?.quantity ?? 0 : product.stockQuantity;
  const maxQuantity = Math.min(100, available);
  const quantity = Number(quantityInput);
  const validQuantity = Number.isInteger(quantity) && quantity >= 1 && quantity <= maxQuantity;
  const selectedBranch = branches.find(value => value.id === branch);
  const hasSelection = selectedBranch && method && validQuantity && (!product.usesVariants || selectedVariant);
  const selection = { productId: id, branchId: branch, playerId: player || null, quantity, ...(variant ? { variantId: variant } : {}) };

  return <section className="detail-card">
    <h2>{product.arabicName}</h2>
    {!locked && <p>المخزون المتاح {product.stockQuantity}</p>}
    {product.isActive && product.stockQuantity > 0 && <div className="form-shell">
      <label>الفرع<select disabled={locked} value={branch} onChange={event => setBranch(event.target.value)}>
        <option value="">اختر الفرع</option>{branches.map(item => <option key={item.id} value={item.id}>{item.arabicName}</option>)}
      </select></label>
      {players.length > 0 && <label>اللاعب — اختياري<select disabled={locked} value={player} onChange={event => setPlayer(event.target.value)}>
        <option value="">بيع مباشر دون لاعب</option>{players.map(item => <option key={item.id} value={item.id}>{item.arabicName}</option>)}
      </select></label>}
      {product.usesVariants && <label>اللون والمقاس والرقم<select disabled={locked} value={variant} onChange={event => { setVariant(event.target.value); setQuantityInput("1"); }}>
        <option value="">اختر من المخزون</option>{product.variants?.filter(item => item.quantity > 0 || item.id === variant).map(item => <option key={item.id} value={item.id}>{variantLabel(item)} · المتاح {item.quantity}</option>)}
      </select></label>}
      <label>الكمية<input disabled={locked} type="number" min="1" max={Math.max(1, maxQuantity)} step="1" value={quantityInput} onChange={event => setQuantityInput(event.target.value)} /></label>
      <label>طريقة الدفع<select disabled={locked} value={method} onChange={event => setMethod(event.target.value as Method)}>
        <option value="">اختر طريقة الدفع</option><option value="Cash">نقدي من وردية مخولة</option><option value="Online">تحويل إلكتروني تم استلامه</option>
      </select></label>
    </div>}
    {!product.isActive ? <p>المنتج متوقف؛ لا يمكن تسجيل بيع جديد.</p>
      : !product.stockQuantity || selectedVariant?.quantity === 0 ? <p role="status">المخزون المطلوب نافد.</p>
      : !branches.length ? <p role="status">لا توجد فروع متاحة للبيع.</p>
      : !validQuantity && (!product.usesVariants || selectedVariant) ? <p role="status">أدخل كمية صحيحة من 1 إلى {maxQuantity}.</p>
      : !hasSelection ? <p>اختر الفرع{product.usesVariants ? " والمقاس" : ""} وطريقة الدفع لمراجعة المبلغ.</p>
      : <ProductSaleConfirmation key={`${branch}:${player}:${quantity}:${method}:${variant}:${refresh}`}
          selection={selection} method={method} product={product.arabicName} branch={selectedBranch.arabicName}
          variant={selectedVariant ? variantLabel(selectedVariant) : undefined}
          onLocked={setLocked} onRefresh={() => { reload(); setRefresh(value => value + 1); }} />}
  </section>;
}

function ProductSaleConfirmation({ selection, method, product, branch, variant, onLocked, onRefresh }: {
  selection: Selection; method: Exclude<Method, "">; product: string; branch: string; variant?: string; onLocked: (value: boolean) => void; onRefresh: () => void;
}) {
  const { productId, branchId, playerId, quantity, variantId } = selection;
  const [quote, setQuote] = useState<Quote | null>(null);
  const [shift, setShift] = useState("");
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [review, setReview] = useState(false);
  const [attempt, setAttempt] = useState<Command | null>(null);
  const [canRetry, setCanRetry] = useState(false);
  const [result, setResult] = useState<SavedSale | null>(null);
  const inflight = useRef(false);

  useEffect(() => {
    const controller = new AbortController();
    const query = new URLSearchParams({ productId, branchId, quantity: String(quantity) });
    if (playerId) query.set("playerId", playerId);
    if (variantId) query.set("variantId", variantId);
    void fetch(`${sales}/quote?${query}`, { cache: "no-store", signal: controller.signal })
      .then(response => readJson(response, "تعذر تحميل المراجعة. حدّث المبلغ والورديات."))
      .then(value => { if (!controller.signal.aborted) setQuote(value); })
      .catch(cause => {
        if (!controller.signal.aborted) setError(cause instanceof DashboardRequestError ? cause.message : "تعذر تحميل المراجعة. حدّث المبلغ والورديات.");
      });
    return () => controller.abort();
  }, [productId, branchId, playerId, quantity, variantId]);

  async function run(task: () => Promise<void>) {
    if (inflight.current) return;
    inflight.current = true;
    setBusy(true);
    setError("");
    try { await task(); }
    finally { inflight.current = false; setBusy(false); }
  }

  function showResult(value: { id?: unknown; status?: unknown; amount?: unknown; currency?: unknown }, command: Command) {
    if (value?.status !== "Confirmed" || typeof value.id !== "string" || !value.id ||
      value.amount !== command.body.expectedAmount || value.currency !== command.currency) throw new Error();
    setResult({ id: value.id, amount: value.amount, currency: value.currency });
    setAttempt(null);
  }

  async function send(command: Command) {
    await run(async () => {
      setAttempt(command);
      setCanRetry(false);
      onLocked(true);
      try {
        const response = await csrfRequest(sales, "POST", command.body, { "Idempotency-Key": command.key }, { announce: false });
        showResult(await response.json(), command);
      } catch (cause) {
        if (cause instanceof DashboardRequestError && [400, 401, 403, 404, 409, 422].includes(cause.status)) {
          setError(cause.message);
          setAttempt(null);
          setQuote(null);
          onLocked(false);
        } else {
          setError("تعذر تأكيد تسجيل البيع. تحقّق من نتيجة المحاولة.");
        }
      }
    });
  }

  async function verify() {
    if (!attempt) return;
    await run(async () => {
      const query = new URLSearchParams({ productId, branchId, requestKey: attempt.key });
      try {
        const response = await fetch(`${sales}/by-request?${query}`, { cache: "no-store" });
        if (response.status === 404) {
          setCanRetry(true);
          setError("لا يوجد بيع مؤكد بهذه المحاولة بعد. يمكنك إعادة إرسال الطلب نفسه.");
          return;
        }
        showResult(await readJson(response, "تعذر التحقق من تسجيل البيع."), attempt);
      } catch (cause) {
        setError(cause instanceof DashboardRequestError ? cause.message : "تعذر التحقق من تسجيل البيع. أعد التحقق.");
      }
    });
  }

  const cash = method === "Cash";
  const ready = quote && (!cash || quote.cashEnabled && quote.sessions.some(session => session.id === shift));
  const shiftName = quote?.sessions.find(session => session.id === shift)?.name;
  const paymentLabel = cash ? `نقدي · ${shiftName ?? ""}` : "تحويل إلكتروني مستلم";

  function prepare(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (ready && !inflight.current && !attempt) setReview(true);
  }

  function confirm() {
    if (!quote || !ready || !review || inflight.current) return;
    setReview(false);
    void send({ key: crypto.randomUUID(), currency: quote.currency,
      body: { ...selection, paymentMethod: method, expectedAmount: quote.amount, cashSessionId: cash ? shift : null },
    });
  }

  if (result) return <section className="detail-card">
    <h2>تم التحصيل وخصم المخزون</h2>
    <p>{formatMoneyAr(result.amount, result.currency)}</p>
    <div className="form-actions"><Link className="primary-button" href={`/dashboard/product-sales/${result.id}`}>تفاصيل العملية</Link><Link className="secondary-button" href="/dashboard/content/catalog">العودة إلى المنتجات</Link></div>
  </section>;

  return <section aria-label="مراجعة تحصيل المنتج">
    {error && <ErrorState message={error} />}
    {attempt ? <div className="detail-card" aria-busy={busy}>
      <h3>نتيجة البيع بانتظار التحقق</h3>
      <div className="form-actions">
        <button type="button" className="primary-button" disabled={busy} onClick={() => void verify()}>التحقق من تسجيل البيع</button>
        {canRetry && <button type="button" className="secondary-button" disabled={busy} onClick={() => void send(attempt)}>إعادة إرسال الطلب نفسه</button>}
      </div>
    </div> : <>
      {!quote && !error && <LoadingState />}
      {quote && <form className="form-shell" onSubmit={prepare}>
        <strong>الإجمالي {formatMoneyAr(quote.amount, quote.currency)}</strong>
        <p>سعر القطعة {formatMoneyAr(quote.unitPrice, quote.currency)} · الكمية {quantity}</p>
        {cash && (quote.cashEnabled ? <label>وردية التحصيل<select required value={shift} onChange={event => setShift(event.target.value)}>
          <option value="">اختر ورديتك المفتوحة</option>{quote.sessions.map(session => <option key={session.id} value={session.id}>{session.name}</option>)}
        </select>{!quote.sessions.length && <small>لا توجد وردية مفتوحة مخولة في فرع البيع وعملة المنتج.</small>}</label> : <p>النقدي غير مفعل لهذه الأكاديمية.</p>)}
        <div className="form-actions"><button className="primary-button" disabled={!ready}>مراجعة التحصيل والبيع</button></div>
      </form>}
      <div className="form-actions">
        <button type="button" onClick={onRefresh}>{cash ? "تحديث المبلغ والورديات" : "تحديث المبلغ والمخزون"}</button>
        {cash && <Link className="secondary-button" href="/dashboard/finance/cash-registers">فتح الصناديق والورديات</Link>}
      </div>
    </>}
    <ConfirmationDialog open={review} title="تأكيد التحصيل والبيع" confirmLabel="تسجيل البيع" busy={busy} tone="primary"
      message={quote ? [product, branch, variant, `الكمية ${quantity}`, paymentLabel, `الإجمالي ${formatMoneyAr(quote.amount, quote.currency)}`].filter(Boolean).join(" · ") + ". أكد استلام المبلغ وتسليم المنتج." : ""}
      onConfirm={confirm} onCancel={() => setReview(false)} />
  </section>;
}
