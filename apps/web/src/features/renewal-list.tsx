"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { AdminDataTable, EmptyState, ErrorState, FilterToolbar, LoadingState, PageHeader, SearchInput, StatusBadge } from "../components/dashboard-shell";
import { formatMoneyAr, workflowStatusLabels } from "../lib/formatters";
import { renewalDetailHref, renewalListHref, renewalListScope } from "../lib/renewal-request-navigation";
import { useGoalData } from "./player-goals";

type RenewalRow = { id: string; player: string; playerCode?: string; sport: string; plan: string; amount: number; currency: string; status: string };

export function RenewalList() {
  const scope = renewalListScope(useSearchParams());
  const { data, error, status, reload } = useGoalData<RenewalRow[]>("/api/v1/subscriptions/renewals", 0, "تعذر تحميل طلبات التجديد.");

  function updateScope(changes: Partial<typeof scope>) {
    const latest = renewalListScope(new URLSearchParams(window.location.search));
    window.history.replaceState(null, "", renewalListHref({ ...latest, ...changes }));
  }

  const term = scope.term.trim().toLocaleLowerCase("ar");
  const filtered = Array.isArray(data) ? data.filter(row => [row.player, row.playerCode, row.sport, row.plan]
    .some(value => typeof value === "string" && value.toLocaleLowerCase("ar").includes(term))) : [];
  const page = Math.min(scope.page, Math.max(1, Math.ceil(filtered.length / scope.pageSize)));
  const offset = (page - 1) * scope.pageSize;

  return <>
    <PageHeader title="طلبات التجديد" context="الأعضاء والاشتراكات" />
    {status === 401 || status === 403 || status === 404 ? <ErrorState message="طلبات التجديد غير متاحة لحسابك." /> :
      error || data && !Array.isArray(data) ? <>
        <ErrorState message="تعذر تحميل طلبات التجديد." />
        <button type="button" className="secondary-button" onClick={reload}>إعادة المحاولة</button>
      </> : !data ? <LoadingState message="جارٍ تحميل طلبات التجديد…" /> : <>
        {(data.length > 0 || scope.term) && <FilterToolbar onReset={scope.term ? () => updateScope({ term: "", page: 1 }) : undefined}>
          <SearchInput value={scope.term} onChange={value => updateScope({ term: value.slice(0, 120), page: 1 })}
            label="بحث طلبات التجديد" placeholder="اسم اللاعب أو كوده أو الرياضة أو الباقة" />
        </FilterToolbar>}
        <section className="list-card">
          {filtered.length === 0 ? <EmptyState message={term ? "لا توجد طلبات تجديد مطابقة للبحث." : "لا توجد طلبات تجديد."} /> :
            <AdminDataTable label="طلبات التجديد" rows={filtered.slice(offset, offset + scope.pageSize)} rowKey={row => row.id}
              columnVisibility={{ hidden: scope.hidden, onChange: hidden => updateScope({ hidden, page }) }}
              serverPagination={{ page: page - 1, pageSize: scope.pageSize, total: filtered.length,
                onPageChange: index => updateScope({ page: index + 1 }), onPageSizeChange: pageSize => updateScope({ pageSize, page: 1 }) }}
              columns={[
                { key: "player", header: "اللاعب", width: "minmax(155px, 1.3fr)", primary: true, render: row =>
                  <Link className="row-primary-action" href={renewalDetailHref(row.id, { ...scope, page })}>
                    {row.player}{row.playerCode && <> · <bdi dir="ltr">{row.playerCode}</bdi></>}
                  </Link>
                },
                { key: "context", header: "الرياضة والباقة", width: "minmax(170px, 1.3fr)", render: row => <>{row.sport}<small>{row.plan}</small></> },
                { key: "amount", header: "المبلغ", width: "minmax(145px, 1fr)", render: row => formatMoneyAr(row.amount, row.currency) },
                { key: "status", header: "الحالة", width: "120px", render: row => <StatusBadge status={row.status} label={workflowStatusLabels[row.status]} /> },
              ]} />}
        </section>
      </>}
  </>;
}
