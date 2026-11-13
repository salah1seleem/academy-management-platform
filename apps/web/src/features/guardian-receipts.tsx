"use client";

import Link from "next/link";
import { useRouter } from "next/navigation";
import { ChevronLeft } from "lucide-react";
import { useEffect } from "react";
import { EmptyState, ErrorState, LoadingState } from "../components/dashboard-shell";
import { formatDateAr, formatDateTimeAr, formatMoneyAr, formatReceiptTimestampAr } from "../lib/formatters";
import { receiptDetailHref, receiptListHref } from "../lib/guardian-receipt-links";
import { receiptAmount, useReceiptRead } from "./receipt-read";

type Receipt = {
  id: string; receiptNumber: string; player: string; sport: string; plan: string;
  amount: number; finalAmount?: number; currency: string; paidAtUtc: string;
  source?: string; linkedChild?: boolean; isVoided?: boolean; currentPlayer?: string | null;
  playerCode?: string | null;
  historicalPaidOn?: string | null;
  paidAtAcademy?: string | null;
};
type Child = { id: string; arabicName: string; playerCode?: string | null };
type Feed = { items: Receipt[]; children: Child[]; total: number; pageSize: number };

function isReceiptFeed(value: unknown): value is Feed {
  if (!value || typeof value !== "object") return false;
  const { items, children, total, pageSize } = value as Partial<Feed>;
  return typeof total === "number" && Number.isSafeInteger(total) && total >= 0 &&
    typeof pageSize === "number" && Number.isSafeInteger(pageSize) && pageSize > 0 &&
    Array.isArray(items) && items.every(item => typeof item?.id === "string" && typeof item.receiptNumber === "string" &&
      typeof item.player === "string" && typeof item.currency === "string" && /^[A-Z]{3}$/.test(item.currency) &&
      receiptAmount(item.finalAmount ?? item.amount) !== null) &&
    Array.isArray(children) && children.every(child => typeof child?.id === "string" && typeof child.arabicName === "string");
}

export function GuardianReceipts({ playerId, page }: { playerId: string; page: number }) {
  const router = useRouter();
  const query = new URLSearchParams({ page: String(page), allIssued: "true" });
  if (playerId) query.set("playerId", playerId);
  const { current, lastData, retry } = useReceiptRead(`/api/v1/guardian/subscriptions/receipts?${query}`, isReceiptFeed);
  const data = current?.data;
  const children = lastData?.children ?? [];
  const selectedChild = children.find(child => child.id === playerId);
  const selectedName = selectedChild?.arabicName;
  const totalPages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

  useEffect(() => {
    if (!data || !window.location.hash.startsWith("#receipt-")) return;
    const link = document.getElementById(window.location.hash.slice(1));
    link?.focus({ preventScroll: true });
    link?.scrollIntoView({ block: "nearest" });
  }, [data]);

  return <>
    <header className="guardian-page-header"><h1>إيصالاتي</h1></header>
    {children.length > 0 && <label className="guardian-receipt-filter">
      اللاعب
      <select value={selectedChild?.id ?? ""} onChange={event => router.push(receiptListHref(event.target.value, 1), { scroll: false })}>
        <option value="">كل الإيصالات</option>
        {children.map(child => <option key={child.id} value={child.id}>
          {child.playerCode ? `\u2066${child.playerCode}\u2069 · ${child.arabicName}` : child.arabicName}
        </option>)}
      </select>
    </label>}
    <section aria-label="إيصالات الدفع" className="guardian-receipt-results">
      {!current ? <LoadingState message="جارٍ تحميل الإيصالات…" />
        : current.error === "network" ? <><ErrorState message="تعذر تحميل الإيصالات." /><button type="button" className="secondary-button" onClick={retry}>إعادة المحاولة</button></>
        : current.error === "unavailable" ? <><EmptyState message="هذه الإيصالات غير متاحة في حسابك الحالي." />{playerId && <Link className="secondary-button" href={receiptListHref("", 1)}>عرض كل الإيصالات</Link>}</>
        : !data?.items?.length ? <EmptyState message={page > 1 ? "لا توجد إيصالات في هذه الصفحة." : playerId ? "لا توجد إيصالات لهذا اللاعب." : "لا توجد إيصالات دفع في حسابك بعد."} />
        : <ul className="guardian-receipt-list">{data.items.map(item => {
          const name = item.currentPlayer || selectedName || item.player;
          const paymentDate = item.historicalPaidOn ? formatDateAr(item.historicalPaidOn) : item.source === "Tournament"
            ? formatDateTimeAr(item.paidAtUtc) : formatReceiptTimestampAr(item.paidAtUtc, item.paidAtAcademy)?.text;
          return <li key={`${item.source ?? "Subscription"}-${item.id}`}>
          <Link id={`receipt-${item.id}`} href={receiptDetailHref(item.id, item.source, playerId, page)}>
            <div className="guardian-receipt-card-heading"><strong>{name}</strong><b>{formatMoneyAr(item.finalAmount ?? item.amount, item.currency, true)}</b></div>
            {item.playerCode && <span>كود اللاعب <bdi dir="ltr">{item.playerCode}</bdi></span>}
            {name !== item.player && <span>الاسم وقت الدفع: {item.player}</span>}
            <span>{[item.source === "Tournament" ? "رسم بطولة" : item.linkedChild === false ? "تجديد لغير الأبناء" : null, item.historicalPaidOn && "تحصيل سابق", item.sport, item.plan].filter(Boolean).join(" · ")}</span>
            {item.isVoided && <strong className="guardian-receipt-void">إيصال معكوس</strong>}
            <div className="guardian-receipt-card-meta">{paymentDate && <time dateTime={item.historicalPaidOn ?? item.paidAtUtc}>{paymentDate}</time>}<bdi>{item.receiptNumber}</bdi><ChevronLeft size={17} aria-hidden /></div>
          </Link>
          </li>;
        })}</ul>}
      {data && page > totalPages && <Link className="secondary-button guardian-receipt-page-return"
        href={receiptListHref(playerId, totalPages)}>العودة إلى الصفحة الأخيرة</Link>}
      {data && page <= totalPages && totalPages > 1 && <nav className="guardian-receipt-pagination" aria-label="صفحات الإيصالات">
        {page > 1 && <Link className="secondary-button" href={receiptListHref(playerId, page - 1)}>السابق</Link>}
        <span>صفحة {page} من {totalPages}</span>
        {page < totalPages && <Link className="secondary-button" href={receiptListHref(playerId, page + 1)}>التالي</Link>}
      </nav>}
    </section>
  </>;
}
