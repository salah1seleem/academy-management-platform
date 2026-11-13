"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useState } from "react";
import { AdminDataTable, EmptyState, ErrorState, FilterToolbar, LoadingState, PageHeader, SearchInput } from "../components/dashboard-shell";
import { formatMoneyAr } from "../lib/formatters";
import { collectionHref, collectionReceiptQuery, collectionScope } from "../lib/subscription-collection-navigation";
import { useReceiptRead } from "./receipt-read";
import styles from "./subscription-collections.module.css";

type CurrencyAmount = { amount: number; currency: string };
type Collection = CurrencyAmount & { id: string; receiptId: string; receiptNumber: string; player: string; sport: string; paymentMethod: string };
type CollectionPage = { totals: CurrencyAmount[]; items: Collection[] };

const paymentMethods = new Map([
  ["Cash", "نقدًا"],
  ["Online", "الدفع الإلكتروني"],
  ["InternalTest", "دفع تجريبي"],
  ["ManualTransfer", "تحويل بنكي"],
  ["BankTransfer", "تحويل بنكي"],
]);

function validAmount(value: CurrencyAmount) {
  return value && Number.isFinite(value.amount) && value.amount >= 0 &&
    typeof value.currency === "string" && /^[A-Z]{3}$/.test(value.currency);
}

function validCollectionPage(data: CollectionPage | undefined): data is CollectionPage {
  return !!data && Array.isArray(data.totals) && Array.isArray(data.items) &&
    data.totals.length <= data.items.length && (data.items.length === 0 || data.totals.length > 0) &&
    data.totals.every(validAmount) && data.items.every(row => validAmount(row) &&
      typeof row.id === "string" && typeof row.receiptId === "string" &&
      /^[\da-f]{8}(?:-[\da-f]{4}){3}-[\da-f]{12}$/i.test(row.receiptId) &&
      typeof row.receiptNumber === "string" && row.receiptNumber.trim().length > 0 &&
      typeof row.player === "string" && typeof row.sport === "string" && typeof row.paymentMethod === "string");
}

export function CollectionList() {
  const scope = collectionScope(useSearchParams());
  const { current, retry } = useReceiptRead<CollectionPage>("/api/v1/subscriptions/collections");

  return <>
    <PageHeader title="تحصيلات الاشتراكات" context="الأعضاء والاشتراكات" />
    {!current ? <LoadingState message="جارٍ تحميل التحصيلات…" /> :
      current.error === "unavailable" ? <ErrorState message="سجل التحصيلات غير متاح لحسابك." /> :
      !validCollectionPage(current.data) ? <>
        <ErrorState message="تعذر تحميل تحصيلات الاشتراكات." />
        <button type="button" className="secondary-button" onClick={retry}>إعادة المحاولة</button>
      </> : <CollectionRows data={current.data} scope={scope} />}
  </>;
}

function CollectionRows({ data, scope }: { data: CollectionPage; scope: ReturnType<typeof collectionScope> }) {
  const [search, setSearch] = useState(scope.term);
  const term = search.trim().toLocaleLowerCase("ar");
  const filtered = term ? data.items.filter(row => [row.player, row.receiptNumber]
    .some(value => value.toLocaleLowerCase("ar").includes(term))) : data.items;
  const page = search === scope.term ? Math.min(scope.page, Math.max(1, Math.ceil(filtered.length / scope.pageSize))) : 1;
  const offset = (page - 1) * scope.pageSize;
  const receiptQuery = collectionReceiptQuery({ ...scope, term: search, page });
  function updateScope(values: Partial<typeof scope>) {
    const latest = collectionScope(new URLSearchParams(window.location.search));
    window.history.replaceState(null, "", collectionHref({ ...latest, ...values }));
  }
  function updateSearch(value: string) {
    const term = value.slice(0, 120);
    setSearch(term);
    updateScope({ term, page: 1 });
  }

  if (!data.items.length) return <EmptyState message="لا توجد تحصيلات اشتراكات في نطاق حسابك." />;

  return <section className="list-card">
    <p className={`metric ${styles.total}`}>
      <span>الإجمالي قبل ردّ المبالغ</span>
      <span className={styles.amounts}>{data.totals.map(total =>
        <strong key={total.currency}>{formatMoneyAr(total.amount, total.currency, true)}</strong>)}</span>
    </p>
    <FilterToolbar onReset={search ? () => updateSearch("") : undefined}>
      <SearchInput label="بحث التحصيلات" placeholder="اسم اللاعب أو رقم الإيصال" value={search} onChange={updateSearch} />
    </FilterToolbar>
    {!filtered.length ? <EmptyState message="لا توجد تحصيلات مطابقة للبحث." /> :
      <AdminDataTable label="تحصيلات الاشتراكات" rows={filtered.slice(offset, offset + scope.pageSize)} rowKey={row => row.id}
        serverPagination={{ page: page - 1, pageSize: scope.pageSize, total: filtered.length,
          onPageChange: index => updateScope({ page: index + 1 }),
          onPageSizeChange: pageSize => updateScope({ pageSize, page: 1 }) }}
        columns={[
          { key: "receipt", header: "الإيصال واللاعب", width: "minmax(190px, 1.5fr)", primary: true, render: row => <>
            <Link className="button button-secondary" aria-label={`عرض الإيصال ${row.receiptNumber}`}
              href={`/dashboard/subscriptions/receipts/${row.receiptId}?${receiptQuery}`}>
              <bdi>{row.receiptNumber}</bdi>
            </Link>
            <small>{row.player}</small>
          </> },
          { key: "sport", header: "الرياضة", render: row => row.sport },
          { key: "amount", header: "المبلغ", render: row => formatMoneyAr(row.amount, row.currency, true) },
          { key: "method", header: "طريقة الدفع", render: row => paymentMethods.get(row.paymentMethod) ?? "غير محددة" },
        ]} />}
  </section>;
}
