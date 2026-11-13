"use client";

import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useEffect, useState } from "react";
import { AdminDataTable, type DataColumn, EmptyState, ErrorState, LoadingState, PageHeader, StatusBadge } from "../components/dashboard-shell";
import { PersonPortrait } from "../components/person-portrait";
import { formatDateAr, formatMoneyAr } from "../lib/formatters";
import { isOwnerHomeDate } from "./owner-home-period";
import styles from "./subscription-home.module.css";

type Enrollment = {
  id: string; sport: string; branch: string; group: string | null;
  active: boolean; frozen?: boolean; endDate: string | null;
  plan?: string; price?: number; currency?: string; remainingSessions?: number; planType?: string;
};
type Row = Enrollment & {
  playerId: string; name: string; playerCode?: string; photoReference: string | null; gender?: string; sports?: Enrollment[];
};
type Snapshot = { items: Row[]; canOpenPlayer?: boolean; total: number; asOf: string; from?: string | null };
type Result = { data: Snapshot } | { error: "network" | "unavailable" | "filters" };
const pageSize = 50;
const labels: Record<string, string> = {
  active: "الاشتراكات السارية",
  current: "الاشتراكات التي لا تنتهي قريبًا",
  expiring: "الاشتراكات التي تنتهي قريبًا",
  expired: "الاشتراكات المنتهية",
};
const dimensions: Record<string, string> = {
  branches: "الاشتراكات حسب الفرع", sports: "الاشتراكات حسب الرياضة",
  packages: "الاشتراكات حسب الباقة", groups: "الاشتراكات حسب المجموعة",
  days: "الاشتراكات حسب أيام التدريب", ages: "الاشتراكات حسب الفئة العمرية",
  months: "تسجيلات بدأ اشتراكها",
};

function enrollmentStatus(enrollment: Enrollment, state: string) {
  if (enrollment.active) return state === "expiring"
    ? <StatusBadge status="Expiring" />
    : <StatusBadge status="Active" label="سارٍ" />;
  return enrollment.frozen
    ? <StatusBadge status="Frozen" label="مجمّد" />
    : <StatusBadge status="Expired" label="منتهٍ" />;
}

function enrollmentBalance(enrollment: Enrollment) {
  const hasSessions = enrollment.planType !== "Duration" && enrollment.remainingSessions != null;
  return <>
    {enrollment.endDate && <time dateTime={enrollment.endDate}>{formatDateAr(enrollment.endDate)}</time>}
    {hasSessions && <span className={styles.snapshotBalance}>الحصص المتبقية {enrollment.remainingSessions}</span>}
    {!enrollment.endDate && !hasSessions && "دون تاريخ نهاية"}
  </>;
}

export function SubscriptionSnapshot() {
  const search = useSearchParams();
  const params = new URLSearchParams([...search.entries()].filter(([, value]) => value !== ""));
  const rawPage = params.get("page") ?? "";
  const requested = /^[1-9]\d{0,5}$/.test(rawPage) ? Number(rawPage) : 1;
  const page = requested <= 100000 ? requested : 1;
  params.delete("page");
  if (params.get("dimension") === "months") params.delete("from");
  return <ScopedSnapshot key={params.toString()} query={params.toString()} page={page} />;
}

function ScopedSnapshot({ query, page }: { query: string; page: number }) {
  const [result, setResult] = useState<{ key: string; value: Result }>();
  const [revision, setRevision] = useState(0);
  const [hiddenColumns, setHiddenColumns] = useState<string[]>([]);
  const params = new URLSearchParams(query);
  const state = params.get("state") ?? "";
  const dimension = params.get("dimension") ?? "";
  const unique = params.get("uniquePlayers") === "true";
  const cumulative = dimension === "months";
  const dimensionLabel = Object.hasOwn(dimensions, dimension) ? dimensions[dimension] : undefined;
  const statusLabel = Object.hasOwn(labels, state) ? labels[state] : undefined;
  const title = unique ? "قائمة اللاعبين" : statusLabel ?? dimensionLabel ?? "الاشتراكات";
  const origin = params.get("origin");
  const returnParams = new URLSearchParams();
  if (params.has("branchId")) returnParams.set("branchId", params.get("branchId")!);
  let back: { href: string; label: string } | undefined;
  const asOf = params.get("to") ?? params.get("asOf");
  const from = params.get("returnFrom") ?? params.get("from");
  if (origin === "owner-cockpit") {
    if (isOwnerHomeDate(from) && isOwnerHomeDate(asOf) && from <= asOf)
      returnParams.set("period-cockpit", `range,${from},${asOf}`);
    else if (params.get("allDates") === "true") returnParams.set("allDates", "true");
    back = { href: `/dashboard?${returnParams}#academy-cockpit`, label: "العودة إلى ملخص الأكاديمية" };
  } else if (origin === "owner-subscriptions") {
    if (params.has("sportId")) returnParams.set("subscriptionSport", params.get("sportId")!);
    if (asOf) returnParams.set("subscriptionTo", asOf);
    returnParams.set("subscriptionFrom", from ?? "");
    if (dimensionLabel) returnParams.set("subscriptionDimension", dimension);
    back = { href: `/dashboard?${returnParams}#academy-subscriptions`, label: "العودة إلى الاشتراكات" };
  }
  const pageParams = (number: number) => {
    const destination = new URLSearchParams(query);
    destination.set("page", String(number));
    return destination.toString();
  };
  const pageHref = (number: number) => `/dashboard/players/subscriptions?${pageParams(number)}`;
  const request = new URLSearchParams(pageParams(page));
  request.delete("origin");
  request.delete("returnFrom");
  const requestKey = request.toString();

  useEffect(() => {
    const controller = new AbortController();
    async function read() {
      let value: Result = { error: "network" };
      try {
        const response = await fetch(`/api/v1/subscriptions/home/items?${requestKey}`, { signal: controller.signal, cache: "no-store" });
        if (response.ok) {
          const data: Snapshot = await response.json();
          if (Array.isArray(data?.items) && Number.isInteger(data.total) && data.total >= 0 && typeof data.asOf === "string") {
            value = { data };
          }
        } else if ([401, 403, 404].includes(response.status)) {
          value = { error: "unavailable" };
        } else if (response.status === 422) {
          value = { error: "filters" };
        }
      } catch {
        // The current request can be retried from the same page and scope.
      }
      if (!controller.signal.aborted) setResult({ key: requestKey, value });
    }
    void read();
    return () => controller.abort();
  }, [requestKey, revision]);

  const header = <PageHeader title={title} context="الأعضاء والاشتراكات" back={back} />;
  const loaded = result?.key === requestKey ? result.value : undefined;
  if (!loaded) return <>{header}<LoadingState /></>;
  if (!("data" in loaded)) return <>{header}<section className="list-card">
    <ErrorState message={{
      network: "تعذر تحميل الاشتراكات.",
      unavailable: "لا يمكن عرض الاشتراكات بهذا النطاق في حسابك الحالي.",
      filters: "تعذر عرض الاشتراكات بهذه الفلاتر.",
    }[loaded.error]} />
    {loaded.error === "network" && <button className="button button-secondary" type="button" onClick={() => { setResult(undefined); setRevision(v => v + 1); }}>إعادة المحاولة</button>}
  </section></>;

  const { data } = loaded;
  const displayCount = new Intl.NumberFormat("ar-EG-u-nu-latn").format(data.total);
  const columns: DataColumn<Row>[] = [
    { key: "player", header: "اللاعب", primary: true, render: player => {
      const identity = <><PersonPortrait photo={player.photoReference ?? undefined} gender={player.gender} size={44} decorative />
        <span className={styles.snapshotIdentity}><span>{player.name}</span>{player.playerCode && <small><bdi>{player.playerCode}</bdi></small>}</span>
      </>;
      return data.canOpenPlayer
        ? <Link className={styles.snapshotPerson} href={`/dashboard/players/${player.playerId}`}>{identity}</Link>
        : <div className={styles.snapshotPerson}>{identity}</div>;
    } },
    ...(unique ? [
      { key: "sports", header: "التسجيلات الرياضية", render: (player: Row) => <ul className={styles.snapshotSportList}>
        {player.sports?.map(enrollment => <li key={enrollment.id}>
          <strong>{enrollment.sport}</strong><small>{enrollment.branch}{enrollment.group && ` · ${enrollment.group}`}</small>
          {!cumulative && enrollmentStatus(enrollment, state)}
          {enrollment.price != null && <small>{formatMoneyAr(enrollment.price, enrollment.currency)}</small>}
          {!cumulative && <small>{enrollmentBalance(enrollment)}</small>}
        </li>)}
      </ul> },
    ] : [
      { key: "sport", header: "الرياضة والباقة", render: (player: Row) => <><strong>{player.sport}</strong>{player.plan && <small>{player.plan}</small>}</> },
      { key: "branch", header: "الفرع والمجموعة", render: (player: Row) => <>{player.branch}<small>{player.group ?? "دون مجموعة"}</small></> },
      ...(!cumulative ? [
        { key: "status", header: "الحالة", render: (player: Row) => enrollmentStatus(player, state) },
        { key: "balance", header: "النهاية والرصيد", render: (player: Row) => enrollmentBalance(player) },
      ] : []),
      { key: "price", header: "قيمة الباقة", render: (player: Row) => player.price != null ? formatMoneyAr(player.price, player.currency) : "غير متاحة" },
    ]),
  ];
  const pages = Math.max(1, Math.ceil(data.total / pageSize));
  return <>{header}
    <section className="list-card" aria-label={title}>
      <p className={styles.snapshotIntro} role="status">
        <span>{unique ? "عدد اللاعبين" : "عدد التسجيلات"} <strong>{displayCount}</strong></span>
        <span>{data.from && data.from !== data.asOf
          ? <>من <time dateTime={data.from}>{formatDateAr(data.from)}</time> إلى </>
          : cumulative ? "حتى " : "الحالة في "}
          <time dateTime={data.asOf}>{formatDateAr(data.asOf)}</time></span>
      </p>
      {data.items.length ? <AdminDataTable label={title} rows={data.items} rowKey={row => row.id} paginate={false} columns={columns}
        columnVisibility={{ hidden: hiddenColumns, onChange: setHiddenColumns }}
        pageRange={{ first: (page - 1) * pageSize, total: data.total }} />
        : <EmptyState message={page > pages ? "هذه الصفحة خارج نتائج الاشتراكات الحالية." : "لا توجد سجلات في هذا النطاق."} />}
      {page > pages
        ? <Link className="button button-secondary" href={pageHref(1)}>الصفحة الأولى</Link>
        : pages > 1 && <nav className={styles.snapshotPages} aria-label="صفحات الاشتراكات">
          {page > 1 && <Link className="button button-secondary" href={pageHref(page - 1)}>الصفحة السابقة</Link>}
          <span>صفحة {page} من {pages}</span>
          {page < pages && <Link className="button button-secondary" href={pageHref(page + 1)}>الصفحة التالية</Link>}
        </nav>}
    </section>
  </>;
}
