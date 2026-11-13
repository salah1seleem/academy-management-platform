"use client";

import Link from "next/link";
import { FormEvent, useEffect, useRef, useState } from "react";
import { useRouter, useSearchParams } from "next/navigation";
import { AdminDataTable, ConfirmationDialog, EmptyState, ErrorState, LoadingState, PageHeader, RowActions, SearchInput, StatusBadge, type DataColumn } from "../components/dashboard-shell";
import { canonicalDigits } from "../lib/number-input";
import { announceSaveSuccess, csrfRequest, DashboardRequestError } from "./dashboard-api";
import { axisLabels } from "./evaluation-axes";
import styles from "./evaluation-criteria.module.css";

const listPath = "/dashboard/evaluations/criteria";
const criteriaApi = "/api/v1/evaluations/criteria";
const optionsApi = "/api/v1/evaluations/options?criteriaOnly=true";
const cannotRead = "تعذر تحميل معايير التقييم.";
const scopeKeys = ["sportId", "active", "search"] as const;

type Sport = { id: string; name: string; supportsFootballAxis?: boolean };
type Criterion = {
  id: string; sportId: string; sport: string; arabicName: string; englishName?: string | null; description?: string | null;
  displayOrder: number; isActive: boolean; weight: number; footballAxis?: string | null; version: number; referenced: boolean;
};
type CriterionWrite = Omit<Criterion, "id" | "sport" | "referenced">;

function readScope(params: Pick<URLSearchParams, "get">): { sportId: string; active: string; search: string } {
  const active = params.get("active");
  return {
    sportId: params.get("sportId") ?? "",
    active: active === "true" || active === "false" ? active : "",
    search: (params.get("search") ?? "").slice(0, 160),
  };
}

function scopedHref(path: string, scope: ReturnType<typeof readScope>) {
  const query = new URLSearchParams();
  for (const key of scopeKeys) if (scope[key]) query.set(key, scope[key]);
  return query.size ? `${path}?${query}` : path;
}

function supportsAxis(sport?: Sport) {
  return sport?.supportsFootballAxis ?? (sport?.name === "كرة القدم");
}

function uncertain(error: unknown) {
  return error instanceof TypeError || (error instanceof DashboardRequestError && error.status >= 500);
}

function message(error: unknown, fallback: string) {
  return error instanceof DashboardRequestError ? error.message : fallback;
}

async function getData<T>(path: string, signal?: AbortSignal): Promise<T> {
  const response = await fetch(path, { cache: "no-store", signal });
  if (!response.ok) throw new DashboardRequestError(cannotRead, response.status);
  return response.json() as Promise<T>;
}

async function getCriteria(signal?: AbortSignal) {
  const rows = await getData<Criterion[]>(criteriaApi, signal);
  if (!Array.isArray(rows)) throw new Error(cannotRead);
  return rows;
}

async function getSports(signal?: AbortSignal) {
  const { sports } = await getData<{ sports: Sport[] }>(optionsApi, signal);
  if (!Array.isArray(sports)) throw new Error(cannotRead);
  return sports;
}

export function CriteriaList() {
  const scope = readScope(useSearchParams());
  const [catalog, setCatalog] = useState<{ rows: Criterion[]; sports: Sport[] }>();
  const [attempt, retry] = useState(0);
  const [error, setError] = useState("");
  const [pending, setPending] = useState<Criterion>();
  const [busy, setBusy] = useState(false);
  const [requiresRead, setRequiresRead] = useState(false);
  const locked = useRef(false);

  useEffect(() => {
    const controller = new AbortController();
    Promise.all([getCriteria(controller.signal), getSports(controller.signal)]).then(([rows, sports]) => {
      if (!controller.signal.aborted) setCatalog({ rows, sports });
    }).catch(() => {
      if (!controller.signal.aborted) setError(cannotRead);
    });
    return () => controller.abort();
  }, [attempt]);

  const sports = new Map(catalog?.sports.map(sport => [sport.id, sport.name]));
  for (const row of catalog?.rows ?? []) if (!sports.has(row.sportId)) sports.set(row.sportId, row.sport);
  const visible = (catalog?.rows ?? []).filter(row =>
    (!scope.sportId || row.sportId === scope.sportId) &&
    (!scope.active || row.isActive === (scope.active === "true")) &&
    (!scope.search.trim() || row.arabicName.includes(scope.search.trim())),
  );

  function updateScope(change: Partial<typeof scope>) {
    const latest = readScope(new URLSearchParams(window.location.search));
    window.history.replaceState(null, "", scopedHref(listPath, { ...latest, ...change }));
  }

  async function confirmStatus() {
    if (!pending || locked.current) return;
    const target = pending;
    locked.current = true;
    setBusy(true);
    setError("");
    try {
      await csrfRequest(`${criteriaApi}/${encodeURIComponent(target.id)}/status`, "PUT",
        { isActive: !target.isActive, version: target.version }, {}, { announce: false });
      const rows = await getCriteria();
      setCatalog(current => current && { ...current, rows });
      const updated = rows.find(row => row.id === target.id);
      if (updated?.isActive === !target.isActive) {
        announceSaveSuccess(updated.isActive ? "تم تفعيل المعيار." : "تم إيقاف المعيار.");
      } else {
        setError("الحالة الحالية للمعيار مختلفة. راجع القائمة.");
      }
    } catch (caught) {
      setRequiresRead(true);
      setError(uncertain(caught) || !(caught instanceof DashboardRequestError)
        ? "تعذر تأكيد الحالة. أعد قراءة القائمة قبل تغييرها مرة أخرى."
        : message(caught, "تعذر تغيير حالة المعيار."));
    } finally {
      setPending(undefined);
      setBusy(false);
      locked.current = false;
    }
  }

  function reload() {
    setError("");
    setRequiresRead(false);
    setCatalog(undefined);
    retry(n => n + 1);
  }

  const columns: DataColumn<Criterion>[] = [
    { key: "name", header: "المعيار", primary: true, width: "minmax(8rem, 1.6fr)", render: row => <strong>{row.arabicName}</strong> },
    { key: "sport", header: "الرياضة", render: row => row.sport },
    { key: "weight", header: "الوزن", render: row => <span><bdi>{row.weight}</bdi></span> },
    { key: "order", header: "الترتيب", render: row => <span><bdi>{row.displayOrder}</bdi></span> },
    { key: "status", header: "الحالة", render: row => <StatusBadge status={row.isActive ? "Active" : "Inactive"} /> },
    { key: "actions", header: "الإجراءات", width: "minmax(9rem, 1.2fr)", render: row => <RowActions actions={[
      { label: "تعديل", href: scopedHref(`${listPath}/${encodeURIComponent(row.id)}/edit`, scope) },
      ...(!requiresRead && !busy ? [{ label: row.isActive ? "إيقاف" : "تفعيل", onClick: () => setPending(row) }] : []),
    ]} /> },
  ];
  if (visible.some(row => row.footballAxis)) {
    columns.splice(2, 0, { key: "axis", header: "المحور", render: row => row.footballAxis ? axisLabels[row.footballAxis] ?? "—" : "—" });
  }

  return <>
    <PageHeader title="معايير التقييم" context="التقييمات / معايير التقييم"
      action={{ label: "إضافة معيار", href: scopedHref(`${listPath}/new`, scope) }} />
    <section className="list-card">
      <div className="player-filters">
        <SearchInput value={scope.search} onChange={search => updateScope({ search: search.slice(0, 160) })} label="بحث باسم المعيار" placeholder="ابحث باسم المعيار" />
        <select aria-label="الرياضة" value={scope.sportId} onChange={e => updateScope({ sportId: e.target.value })}>
          <option value="">كل الرياضات</option>
          {catalog && scope.sportId && !sports.has(scope.sportId) && <option value={scope.sportId}>رياضة غير متاحة</option>}
          {[...sports].map(([id, name]) => <option key={id} value={id}>{name}</option>)}
        </select>
        <select aria-label="الحالة" value={scope.active} onChange={e => updateScope({ active: e.target.value })}>
          <option value="">كل الحالات</option><option value="true">فعال</option><option value="false">متوقف</option>
        </select>
      </div>
      {error && <><ErrorState message={error} /><button type="button" className="button button-secondary" onClick={reload}>إعادة قراءة القائمة</button></>}
      {!catalog ? !error && <LoadingState message="جارٍ تحميل معايير التقييم…" />
        : visible.length ? <AdminDataTable label="معايير التقييم" rows={visible} rowKey={row => row.id} columns={columns} />
        : <EmptyState message={scope.sportId || scope.active || scope.search.trim() ? "لا توجد معايير مطابقة للفلاتر." : "لا توجد معايير تقييم بعد."} />}
    </section>
    <ConfirmationDialog open={!!pending} busy={busy} tone={pending?.isActive ? "danger" : "primary"}
      title={pending?.isActive ? "إيقاف المعيار؟" : "تفعيل المعيار؟"}
      message={pending ? `«${pending.arabicName}» ${pending.isActive ? "لن يُضاف إلى التقييمات الجديدة." : "سيظهر في التقييمات الجديدة."}` : ""}
      confirmLabel={pending?.isActive ? "تأكيد الإيقاف" : "تأكيد التفعيل"}
      onCancel={() => setPending(undefined)} onConfirm={() => void confirmStatus()} />
  </>;
}

function normalizeNumber(value: FormDataEntryValue | null) {
  return canonicalDigits(String(value ?? "").trim()).replace(/[٫,،]/g, ".");
}

function payload(form: HTMLFormElement, sportId: string, current: Criterion | undefined, axisAllowed: boolean): CriterionWrite | string {
  const data = new FormData(form);
  const text = (key: string) => String(data.get(key) ?? "").trim();
  const weightText = normalizeNumber(data.get("weight"));
  const orderText = normalizeNumber(data.get("displayOrder"));
  const weight = Number(weightText);
  const displayOrder = Number(orderText);
  if (!sportId) return "اختر الرياضة.";
  if (!text("arabicName")) return "اكتب اسم المعيار بالعربية.";
  if (!/^\d{1,5}(?:\.\d{1,3})?$/.test(weightText) || weight <= 0) return "أدخل وزنًا من 0.001 إلى 99999.999.";
  if (!/^\d+$/.test(orderText) || displayOrder > 2147483647) return "أدخل رقم ترتيب صحيحًا من صفر إلى 2147483647.";
  return {
    sportId, arabicName: text("arabicName"), englishName: text("englishName") || null, description: text("description") || null,
    footballAxis: axisAllowed ? text("footballAxis") || null : null,
    weight, displayOrder, isActive: data.get("isActive") === "on", version: current?.version ?? 0,
  };
}

function sameValues(row: Criterion, write: CriterionWrite) {
  return row.sportId === write.sportId && row.arabicName === write.arabicName &&
    (row.englishName ?? null) === write.englishName && (row.description ?? null) === write.description &&
    (row.footballAxis ?? null) === write.footballAxis && row.weight === write.weight &&
    row.displayOrder === write.displayOrder && row.isActive === write.isActive;
}

export function CriterionForm({ id }: { id?: string }) {
  const router = useRouter();
  const scope = readScope(useSearchParams());
  const back = { href: scopedHref(listPath, scope), label: "العودة إلى معايير التقييم" };
  const title = id ? "تعديل معيار" : "إضافة معيار";
  const [attempt, retry] = useState(0);
  const [setup, setSetup] = useState<{ sports: Sport[]; current?: Criterion }>();
  const [readError, setReadError] = useState("");
  const [canRetryRead, setCanRetryRead] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    const { signal } = controller;
    Promise.all([getSports(signal), id ? getCriteria(signal) : Promise.resolve([])]).then(([sports, rows]) => {
      if (signal.aborted) return;
      const current = rows.find(row => row.id.toLowerCase() === id?.toLowerCase());
      if (id && !current) {
        setReadError("هذا المعيار غير متاح.");
        return;
      }
      setSetup({ sports, current });
    }).catch(caught => {
      if (signal.aborted) return;
      const denied = caught instanceof DashboardRequestError && [401, 403, 404].includes(caught.status);
      setReadError(denied ? "معايير التقييم غير متاحة لحسابك." : "تعذر تحميل بيانات المعيار.");
      setCanRetryRead(!denied);
    });
    return () => controller.abort();
  }, [id, attempt]);

  function reload() {
    setSetup(undefined);
    setReadError("");
    setCanRetryRead(false);
    retry(n => n + 1);
  }

  return <>
    <PageHeader title={title} context="التقييمات / معايير التقييم" back={back} />
    {setup ? <CriterionFields key={`${id ?? "new"}:${attempt}`} {...setup} initialSportId={scope.sportId} backHref={back.href}
      onSaved={notice => { announceSaveSuccess(notice); router.push(back.href); }} onReload={reload} />
      : readError ? <><ErrorState message={readError} />
        {canRetryRead && <button type="button" className="button button-secondary" onClick={reload}>إعادة المحاولة</button>}</>
      : <LoadingState message="جارٍ تحميل بيانات المعيار…" />}
  </>;
}

function CriterionFields({ sports, current, initialSportId, backHref, onSaved, onReload }: {
  sports: Sport[]; current?: Criterion; initialSportId: string; backHref: string; onSaved: (notice: string) => void; onReload: () => void;
}) {
  const [sportId, setSportId] = useState(current?.sportId ??
    (sports.some(sport => sport.id === initialSportId) ? initialSportId : sports.length === 1 ? sports[0].id : ""));
  const [axis, setAxis] = useState(current?.footballAxis ?? "");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [pending, setPending] = useState<CriterionWrite>();
  const [outdated, setOutdated] = useState(false);
  const [confirmReload, setConfirmReload] = useState(false);
  const locked = useRef(false);
  const sport = sports.find(sport => sport.id === sportId);
  const showAxis = supportsAxis(sport) || (sport?.supportsFootballAxis === undefined && !!current?.footballAxis);

  async function save(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (locked.current || pending || outdated || !sport) return;
    const write = payload(event.currentTarget, sportId, current, showAxis);
    if (typeof write === "string") { setError(write); return; }
    locked.current = true;
    setBusy(true);
    setError("");
    try {
      await csrfRequest(current ? `${criteriaApi}/${encodeURIComponent(current.id)}` : criteriaApi,
        current ? "PUT" : "POST", write, {}, { announce: false });
      onSaved(current ? "تم حفظ المعيار." : "تمت إضافة المعيار.");
    } catch (caught) {
      if (uncertain(caught)) {
        setPending(write);
        setError("تعذر تأكيد الحفظ. تحقّق من القائمة قبل إعادة الإرسال.");
      } else if (caught instanceof DashboardRequestError && caught.status === 409 && current) {
        setPending(write);
        setError(caught.message);
      } else {
        setError(message(caught, "تعذر حفظ المعيار."));
      }
    } finally {
      locked.current = false;
      setBusy(false);
    }
  }

  async function verify() {
    if (!pending || locked.current) return;
    locked.current = true;
    setBusy(true);
    try {
      const rows = await getCriteria();
      const stored = current ? rows.find(row => row.id === current.id) : rows.find(row =>
        row.sportId === pending.sportId && row.arabicName === pending.arabicName);
      if (stored && sameValues(stored, pending)) {
        onSaved("القيم المطلوبة موجودة في قائمة المعايير.");
      } else if ((current && stored?.version === current.version) || (!current && !stored)) {
        setPending(undefined);
        setError("لم تظهر القيم المطلوبة في القائمة. راجع الحقول قبل الحفظ.");
      } else if (current) {
        setPending(undefined);
        setOutdated(true);
        setError("تغيّر المعيار منذ فتح النموذج. حمّل النسخة الحالية قبل الحفظ.");
      } else {
        setPending(undefined);
        setError("يوجد معيار بالاسم نفسه لهذه الرياضة. راجع الاسم قبل الحفظ.");
      }
    } catch {
      setError("تعذر قراءة قائمة المعايير. أعد التحقق.");
    } finally {
      locked.current = false;
      setBusy(false);
    }
  }

  if (!sports.length && !current) return <EmptyState message="لا توجد رياضات فعّالة لإضافة معيار." />;

  return <>
    <form className="form-shell" onSubmit={save} noValidate>
      <div className={styles.fields}>
        {current ? <dl className={styles.sport}><dt>الرياضة</dt><dd>{current.sport}</dd></dl>
          : <label>الرياضة<select name="sportId" value={sportId} required disabled={busy || !!pending}
            onChange={e => { setSportId(e.target.value); setAxis(""); }}>
            <option value="">اختر الرياضة</option>{sports.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
          </select></label>}
        <label>الاسم بالعربية<input name="arabicName" defaultValue={current?.arabicName ?? ""} maxLength={160} required readOnly={busy || !!pending} /></label>
        {showAxis && <label>محور كرة القدم <select name="footballAxis" value={axis} onChange={e => setAxis(e.target.value)} disabled={busy || !!pending}>
          <option value="">بدون محور</option>{Object.entries(axisLabels).map(([value, label]) => <option key={value} value={value}>{label}</option>)}
        </select></label>}
        <div className={styles.numbers}>
          <label>الوزن<input name="weight" inputMode="decimal" defaultValue={current?.weight ?? 1} required readOnly={busy || !!pending} /></label>
          <label>الترتيب<input name="displayOrder" inputMode="numeric" defaultValue={current?.displayOrder ?? 0} required readOnly={busy || !!pending} /></label>
        </div>
        <label className="check-field"><input type="checkbox" name="isActive" defaultChecked={current?.isActive ?? true} disabled={busy || !!pending} /> إتاحة المعيار في التقييمات الجديدة</label>
      </div>
      <details open={!!(current?.englishName || current?.description)}>
        <summary>الاسم بالإنجليزية والوصف</summary>
        <div className={styles.optionalFields}>
          <label>الاسم بالإنجليزية<input name="englishName" dir="ltr" defaultValue={current?.englishName ?? ""} maxLength={160} readOnly={busy || !!pending} /></label>
          <label>الوصف<textarea name="description" defaultValue={current?.description ?? ""} maxLength={600} readOnly={busy || !!pending} /></label>
        </div>
      </details>
      {current && !sport && <ErrorState message="رياضة المعيار متوقفة. فعّل الرياضة قبل تعديل المعيار." />}
      {error && <ErrorState message={error} />}
      <div className="form-actions">
        {pending ? <button type="button" className="button button-primary" onClick={() => void verify()} disabled={busy}>التحقق من الحفظ</button>
          : outdated ? <button type="button" className="button button-primary" onClick={() => setConfirmReload(true)}>تحميل النسخة الحالية</button>
          : <button className="primary-button" disabled={busy || !sport}>{busy ? "جارٍ الحفظ…" : "حفظ المعيار"}</button>}
        <Link href={backHref} className="button button-secondary">إلغاء</Link>
      </div>
    </form>
    <ConfirmationDialog open={confirmReload} title="تحميل النسخة الحالية؟"
      message="سيُستبدل ما أدخلته بالقيم المحفوظة في القائمة." confirmLabel="تحميل النسخة الحالية"
      onCancel={() => setConfirmReload(false)} onConfirm={() => { setConfirmReload(false); onReload(); }} />
  </>;
}
