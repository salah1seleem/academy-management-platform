"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { AdminDataTable, ConfirmationDialog, EmptyState, ErrorState, FilterToolbar, LoadingState, PageHeader, RowActions, SearchInput, StatusBadge } from "../components/dashboard-shell";
import { formatMoneyAr, planTypeLabel } from "../lib/formatters";
import { announceSaveSuccess, csrfRequest, DashboardRequestError } from "./dashboard-api";
import { planCatalogHref, planCatalogPath, planLimits, planServiceLabels } from "./plan-catalog-values";
import { useStaffAction } from "./use-staff-action";

type Plan = {
  id: string; arabicName: string; sport: string; planType: string; price: number; currency: string;
  durationDays?: number | null; durationMonths?: number | null; sessionCount?: number | null; serviceKind?: string; isActive: boolean;
  canManagePublication?: boolean;
};
const endpoint = "/api/v1/subscriptions/plans";

async function readPlans(signal?: AbortSignal): Promise<Plan[]> {
  const response = await fetch(endpoint, { signal, cache: "no-store" });
  if (response.status === 403) throw new Error("لا تملك صلاحية عرض الباقات.");
  if (!response.ok) throw new Error("تعذر تحميل الباقات.");
  const plans: unknown = await response.json();
  if (!Array.isArray(plans)) throw new Error("تعذر تحميل الباقات.");
  return plans as Plan[];
}

export function PlanList() {
  const params = useSearchParams();
  const [search, setSearch] = useState(params.get("q") ?? "");
  const canAdd = useStaffAction("subscriptions", "add");
  const canEdit = useStaffAction("subscriptions", "edit");
  const [plans, setPlans] = useState<Plan[]>();
  const [revision, setRevision] = useState(0);
  const [readError, setReadError] = useState("");
  const [actionError, setActionError] = useState("");
  const [selected, setSelected] = useState<Plan>();
  const [uncertain, setUncertain] = useState<Plan>();
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    void readPlans(controller.signal).then(value => {
      if (!controller.signal.aborted) setPlans(value);
    }).catch((error: unknown) => {
      if (!controller.signal.aborted) setReadError(error instanceof Error && error.message === "لا تملك صلاحية عرض الباقات." ? error.message : "تعذر تحميل الباقات.");
    });
    return () => controller.abort();
  }, [revision]);

  function filter(value: string) {
    setSearch(value);
    const url = new URL(window.location.href);
    if (value.trim()) url.searchParams.set("q", value.trim());
    else url.searchParams.delete("q");
    window.history.replaceState(null, "", `${url.pathname}${url.search}${url.hash}`);
  }

  function saved(plan: Plan) {
    announceSaveSuccess(plan.isActive ? "تم إيقاف الباقة مع حفظ سجلها." : "تم تفعيل الباقة.");
  }

  async function changeStatus() {
    if (!selected || busy || !canEdit) return;
    const plan = selected;
    setBusy(true);
    setActionError("");
    try {
      await csrfRequest(`${endpoint}/${plan.id}/status`, "PUT", { isActive: !plan.isActive }, {}, { announce: false });
      setPlans(current => current?.map(row => row.id === plan.id ? { ...row, isActive: !plan.isActive } : row));
      saved(plan);
    } catch (error) {
      if (error instanceof DashboardRequestError && error.status > 0 && error.status < 500) {
        setActionError(error.message);
      } else {
        setUncertain(plan);
        setActionError(`تعذر تأكيد حالة «${plan.arabicName}». تحقّق من الحالة قبل تنفيذ إجراء آخر عليها.`);
      }
    } finally {
      setSelected(undefined);
      setBusy(false);
    }
  }

  async function verifyStatus() {
    if (!uncertain || busy) return;
    setBusy(true);
    try {
      const fresh = await readPlans();
      const verified = fresh.find(plan => plan.id === uncertain.id);
      setPlans(fresh);
      setUncertain(undefined);
      if (verified && verified.isActive !== uncertain.isActive) {
        saved(uncertain);
        setActionError("");
      } else {
        setActionError(verified ? "حالة الباقة كما كانت. راجعها قبل إعادة الإجراء." : "الباقة غير متاحة الآن في قائمتك.");
      }
    } catch {
      setActionError("تعذّر التحقق من حالة الباقة. أعد المحاولة.");
    } finally {
      setBusy(false);
    }
  }

  const term = search.trim().toLocaleLowerCase("ar");
  const visible = plans?.filter(plan => `${plan.arabicName} ${plan.sport}`.toLocaleLowerCase("ar").includes(term));
  const href = (path: string) => planCatalogHref(path, search);

  return <>
    <PageHeader title="باقات الاشتراك" context="الاشتراكات" action={canAdd ? { label: "إضافة باقة", href: href(`${planCatalogPath}/new`) } : undefined} />
    <FilterToolbar onReset={term ? () => filter("") : undefined}>
      <SearchInput value={search} onChange={filter} label="بحث في الباقات" placeholder="اسم الباقة أو الرياضة" />
    </FilterToolbar>
    {actionError && <ErrorState message={actionError} />}
    {uncertain && <button type="button" className="button button-secondary" disabled={busy} onClick={() => void verifyStatus()}>
      {busy ? "جارٍ التحقق…" : "تحقّق من حالة الباقة"}
    </button>}
    <section className="list-card">
      {readError ? <><ErrorState message={readError} /><button type="button" className="button button-secondary" onClick={() => { setReadError(""); setRevision(value => value + 1); }}>إعادة تحميل الباقات</button></>
        : !visible ? <LoadingState message="جارٍ تحميل الباقات…" />
        : !visible.length ? <EmptyState message={term ? "لا توجد باقات تطابق البحث." : "لا توجد باقات اشتراك بعد."} />
        : <AdminDataTable label="باقات الاشتراك" rows={visible} rowKey={plan => plan.id} columns={[
          { key: "name", header: "الباقة", primary: true, render: plan => <><Link href={href(`${planCatalogPath}/${plan.id}`)}><strong>{plan.arabicName}</strong></Link><small>{plan.sport}{plan.serviceKind && plan.serviceKind !== "Training" ? ` · ${planServiceLabels[plan.serviceKind] ?? "خدمة"}` : ""}</small></> },
          { key: "terms", header: "شروط الباقة", render: plan => {
            const limits = planLimits(plan);
            return <>{planTypeLabel(plan.planType)}{limits && <small>{limits}</small>}</>;
          } },
          { key: "price", header: "السعر", render: plan => <strong>{formatMoneyAr(plan.price, plan.currency)}</strong> },
          { key: "status", header: "الحالة", render: plan => <StatusBadge status={plan.isActive ? "Active" : "Inactive"} label={plan.isActive ? "متاحة" : "موقوفة"} /> },
          ...(canEdit && visible.some(plan => plan.canManagePublication !== false) ? [{ key: "actions", header: "الإجراءات", width: "minmax(130px, 1fr)", render: (plan: Plan) => plan.canManagePublication === false ? null : <RowActions actions={[
            { label: "تعديل", href: href(`${planCatalogPath}/${plan.id}/edit`) },
            ...(!uncertain ? [{ label: plan.isActive ? "إيقاف" : "تفعيل", onClick: () => { setActionError(""); setSelected(plan); } }] : []),
          ]} /> }] : []),
        ]} />}
    </section>
    {selected && <ConfirmationDialog open title={selected.isActive ? `إيقاف «${selected.arabicName}»؟` : `تفعيل «${selected.arabicName}»؟`}
      message={selected.isActive ? "ستختفي الباقة من خيارات الاشتراك والتجديد الجديدة، وتبقى الاشتراكات الحالية وسجل الأسعار محفوظين." : "ستظهر الباقة في خيارات الاشتراك والتجديد للفروع المنشورة فيها."}
      confirmLabel={selected.isActive ? "إيقاف الباقة" : "تفعيل الباقة"} tone={selected.isActive ? "danger" : "primary"}
      busy={busy} onConfirm={() => void changeStatus()} onCancel={() => setSelected(undefined)} />}
  </>;
}
