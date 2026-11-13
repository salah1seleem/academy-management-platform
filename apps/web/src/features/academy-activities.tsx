"use client";

import { ArrowRight, CalendarDays, ChevronLeft, Newspaper } from "lucide-react";
import Link from "next/link";
import { useEffect, useState } from "react";
import { EmptyState, ErrorState, LoadingState } from "../components/dashboard-shell";
import { formatDateAr } from "../lib/formatters";
import styles from "./academy-activities.module.css";

type Item = {
  id: string;
  title: string;
  body: string;
  kind: "News" | "Announcement" | "Event";
  publishedAtUtc: string;
  imageUrl?: string;
};

type Feed = { items: Item[]; total: number; pageSize: number };
export type ActivityKind = "News" | "Event";
export type ActivitySearch = { kind?: string; item?: string; page?: string };
type ReadFailure = "unavailable" | "network";

const labels = { News: "خبر", Announcement: "إعلان", Event: "فعالية" };

function Cover({ item }: { item: Item }) {
  const [failed, setFailed] = useState<string>();
  const image = item.imageUrl;
  return <div className={styles.cover}>
    {image && failed !== image && /^\/api\/v1\/assets\/[\da-f-]{36}$/i.test(image)
      // The image requires the current authenticated browser session.
      // eslint-disable-next-line @next/next/no-img-element
      ? <img src={image} alt="" loading="lazy" onError={() => setFailed(image)} />
      : item.kind === "Event" ? <CalendarDays aria-hidden /> : <Newspaper aria-hidden />}
  </div>;
}

function Publication({ item }: { item: Item }) {
  return <small className={styles.publication}>
    <span>{labels[item.kind] ?? "نشاط"}</span>
    {Number.isFinite(Date.parse(item.publishedAtUtc)) && <span>نُشر في <time dateTime={item.publishedAtUtc}>{formatDateAr(item.publishedAtUtc)}</time></span>}
  </small>;
}

function Caption({ item }: { item: Item }) {
  return <div className={styles.caption}>
    <Publication item={item} />
    <div><strong>{item.title}</strong><ChevronLeft size={16} aria-hidden /></div>
  </div>;
}

function useActivityRead<T>(path: string | null, decode: (value: unknown) => T | undefined) {
  const [revision, setRevision] = useState(0);
  const [result, setResult] = useState<{ key: string; data?: T; error?: ReadFailure }>();
  const key = `${path}:${revision}`;
  useEffect(() => {
    if (!path) return;
    const request = new AbortController();
    void fetch(path, { signal: request.signal, cache: "no-store" })
      .then(async response => {
        if ([401, 403, 404].includes(response.status)) {
          if (!request.signal.aborted) setResult({ key, error: "unavailable" });
          return;
        }
        if (!response.ok) throw new Error();
        const data = decode(await response.json());
        if (!data) throw new Error();
        if (!request.signal.aborted) setResult({ key, data });
      })
      .catch(() => { if (!request.signal.aborted) setResult({ key, error: "network" }); });
    return () => request.abort();
  }, [key, path, decode]);
  return { current: result?.key === key ? result : undefined, retry: () => setRevision(value => value + 1) };
}

function previewResult(value: unknown): Item[] | undefined {
  return Array.isArray(value) ? value : undefined;
}

function feedResult(value: unknown): Feed | undefined {
  const data = value as Feed | null;
  return data && Array.isArray(data.items) && Number.isSafeInteger(data.total) && Number.isSafeInteger(data.pageSize) && data.pageSize > 0 ? data : undefined;
}

function activityUrl(base: string, kind: ActivityKind, page: number, item?: string) {
  const query = new URLSearchParams({ kind });
  if (page > 1) query.set("page", String(page));
  if (item) query.set("item", item);
  return `${base}?${query}`;
}

function activityCardUrl(base: string, item: Item, page = 1) {
  return activityUrl(base, item.kind === "Event" ? "Event" : "News", page, item.id);
}

export function AcademyActivityPreview({ guardian = false }: { guardian?: boolean }) {
  const { current, retry } = useActivityRead("/api/v1/news", previewResult);
  const href = guardian ? "/guardian/activities" : "/dashboard/activities";
  const items = current?.data ?? [];

  if (!items.length && current?.error !== "network") return null;

  return <section className={styles.section} aria-label="الأخبار والفعاليات">
    <header className={styles.header}>
      <h2>الأخبار والفعاليات</h2>
      <Link href={href}>كل الأنشطة <ChevronLeft size={16} aria-hidden /></Link>
    </header>
    {current?.error === "network" ? <ReadError retry={retry} /> : <div className={styles.rail}>
      {items.map(item => <Link
        key={item.id}
        className={styles.card}
        href={activityCardUrl(href, item)}
      >
        <Cover item={item} />
        <Caption item={item} />
      </Link>)}
    </div>}
  </section>;
}

function ReadError({ retry }: { retry: () => void }) {
  return <div className={styles.error}><ErrorState message="تعذر تحميل الأنشطة." />
    <button type="button" onClick={retry}>إعادة المحاولة</button></div>;
}

export function AcademyActivities({ search, guardian = false }: { search: ActivitySearch; guardian?: boolean }) {
  const base = guardian ? "/guardian/activities" : "/dashboard/activities";
  const kind = search.kind === "Event" ? "Event" : "News";
  const requestedPage = Number(search.page);
  const page = Number.isSafeInteger(requestedPage) && requestedPage >= 1 && requestedPage <= 100000 ? requestedPage : 1;
  const itemId = typeof search.item === "string" ? search.item : undefined;
  const itemValid = itemId === undefined || /^[a-f\d]{8}(?:-[a-f\d]{4}){3}-[a-f\d]{12}$/i.test(itemId);
  const query = new URLSearchParams({kind, page: String(page)});
  if (itemId && itemValid) query.set("item", itemId);
  const { current, retry } = useActivityRead(itemValid ? `/api/v1/news?${query}` : null, feedResult);
  const feed = current?.data;
  const selected = itemId && itemValid ? feed?.items.find(item => item.id.toLowerCase() === itemId.toLowerCase()) : undefined;
  const header = guardian ? "guardian-page-header" : styles.feedHeader;
  const returnToList = activityUrl(base, kind, page) + (itemId && itemValid ? `#activity-${itemId.toLowerCase()}` : "");

  useEffect(() => {
    if (!feed || itemId !== undefined || !window.location.hash.startsWith("#activity-")) return;
    const card = document.getElementById(window.location.hash.slice(1));
    card?.focus({ preventScroll: true });
    card?.scrollIntoView({ block: "nearest" });
  }, [feed, itemId]);

  if (itemId !== undefined) return <section className={styles.section} dir="rtl">
    <header className={header}>
      <Link className={styles.back} href={returnToList}><ArrowRight size={18} aria-hidden /> العودة إلى {kind === "Event" ? "الفعاليات" : "الأخبار"}</Link>
    </header>
    {!itemValid || current?.error === "unavailable" || (feed && !selected) ? <EmptyState message="هذا النشاط غير متاح." />
      : current?.error === "network" ? <ReadError retry={retry} />
      : !selected ? <LoadingState message="جارٍ تحميل النشاط…" /> : <article className={`${styles.card} ${styles.detail}`}>
        <header><h1>{selected.title}</h1><Publication item={selected} /></header>
        {selected.imageUrl && <Cover item={selected} />}
        <p>{selected.body}</p>
      </article>}
  </section>;

  const totalPages = feed ? Math.max(1, Math.ceil(feed.total / feed.pageSize)) : 1;
  return <section className={styles.section} dir="rtl">
    <header className={header}><h1>الأخبار والفعاليات</h1></header>
    <nav className={guardian ? `${styles.tabs} ${styles.guardianTabs}` : styles.tabs} aria-label="نوع النشاط">
      {([["News", "الأخبار"], ["Event", "الفعاليات"]] as const).map(([value, label]) =>
        <Link key={value} href={activityUrl(base, value, 1)} aria-current={kind === value ? "page" : undefined} scroll={false}>{label}</Link>
      )}
    </nav>
    {!current ? <LoadingState message="جارٍ تحميل الأنشطة…" /> : current.error === "unavailable" ? <EmptyState message="الأخبار والفعاليات غير متاحة في هذه الجلسة." />
      : current.error === "network" ? <ReadError retry={retry} />
      : !feed?.items.length ? <EmptyState message={page > 1 ? "لا توجد أنشطة في هذه الصفحة." : kind === "Event" ? "لا توجد فعاليات منشورة." : "لا توجد أخبار منشورة."} />
      :
        <div className={styles.grid}>
          {feed.items.map(item => <Link key={item.id} id={`activity-${item.id.toLowerCase()}`} className={styles.card} href={activityCardUrl(base, item, page)}>
            <Cover item={item} />
            <Caption item={item} />
          </Link>)}
        </div>}
    {feed && (page > 1 || totalPages > 1) && <nav className={styles.pages} aria-label="صفحات الأنشطة">
      {page > 1 && <Link href={activityUrl(base, kind, page - 1)}>السابق</Link>}
      <span>صفحة {page}</span>
      {page < totalPages && <Link href={activityUrl(base, kind, page + 1)}>التالي</Link>}
    </nav>}
  </section>;
}
