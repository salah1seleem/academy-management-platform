import {CalendarDays, Check, MapPin} from "lucide-react";
import Image from "next/image";
import {useEffect, useId, useRef, useState} from "react";
import {formatDateAr} from "../lib/formatters";
import styles from "./owner-home-refinement.module.css";

export type PeriodPreset = "range" | "today" | "month";
export type DateBounds = {from: string; to: string};
export type HomePeriod = {preset: PeriodPreset; range: DateBounds};
export type Branch = {id: string; arabicName: string};
const maxPeriodDays = 366;

export function isOwnerHomeDate(value: string | null): value is string {
  if (!value || !/^(20\d{2}|2100)-\d{2}-\d{2}$/.test(value)) return false;
  const date = new Date(`${value}T00:00:00Z`);
  return Number.isFinite(date.valueOf()) && date.toISOString().startsWith(value);
}

export function ownerPeriodError(range: DateBounds): string | undefined {
  if (!isOwnerHomeDate(range.from) || !isOwnerHomeDate(range.to)) return "اختر تاريخ البداية والنهاية.";
  if (range.from > range.to) return "تاريخ النهاية يجب ألا يسبق البداية.";
  if (Date.parse(range.to) - Date.parse(range.from) > maxPeriodDays * 86400000) {
    return "اختر فترة لا تتجاوز 366 يومًا.";
  }
}


export function selectedDates(period: HomePeriod, today: string): DateBounds {
  if (period.preset === "today") return {from: today, to: today};
  if (period.preset === "month") return {from: `${today.slice(0, 7)}-01`, to: today};
  return period.range;
}

function appliedPeriodLabel({from, to}: DateBounds) {
  if (from && to) return from === to ? formatDateAr(to) : `من ${formatDateAr(from)} إلى ${formatDateAr(to)}`;
  if (to) return `حتى ${formatDateAr(to)}`;
  if (from) return `من ${formatDateAr(from)}`;
  return "كل التواريخ";
}

export function OwnerHomePeriodControls({name, value, today, onChange}: {
  name: string;
  value: HomePeriod;
  today: string;
  onChange: (period: HomePeriod) => void;
}) {
  const [draft, setDraft] = useState<DateBounds>();
  const [checked, setChecked] = useState(false);
  const buttonRef = useRef<HTMLButtonElement>(null);
  const dateRef = useRef<HTMLInputElement>(null);
  const id = useId();
  const editing = !!draft;
  const error = draft && checked ? ownerPeriodError(draft) : undefined;
  useEffect(() => {
    if (editing) dateRef.current?.focus();
  }, [editing]);

  function close() {
    setDraft(undefined);
    setChecked(false);
    buttonRef.current?.focus();
  }

  return <div className={styles.periodControls} role="group" aria-label={`فترة ${name}`}>
    <p className={styles.periodCaption} aria-label={`الفترة المطبقة (${name})`}>
      {appliedPeriodLabel(selectedDates(value, today))}
    </p>
    <div className={styles.periodChoices}>
      <button ref={buttonRef} type="button" data-selected={value.preset === "range"}
        aria-expanded={editing} aria-controls={editing ? id : undefined} onClick={() => {
          if (editing) close();
          else setDraft({...value.range});
        }}><CalendarDays size={16} aria-hidden="true"/>الفترة المخصصة</button>
      {([["today", "هذا اليوم"], ["month", "من أول الشهر"]] as const).map(([preset, label]) =>
        <button key={preset} type="button" aria-pressed={value.preset === preset} onClick={() => {
          setDraft(undefined);
          setChecked(false);
          if (preset !== value.preset) onChange({...value, preset});
        }}>{label}</button>
      )}
    </div>
    {draft && <form id={id} className={styles.periodEditor} aria-label={`تعديل فترة ${name}`}
      onKeyDown={event => {if (event.key === "Escape") {event.stopPropagation(); close();}}}
      onSubmit={event => {
        event.preventDefault();
        setChecked(true);
        if (ownerPeriodError(draft)) return;
        if (value.preset !== "range" || draft.from !== value.range.from || draft.to !== value.range.to) {
          onChange({preset: "range", range: draft});
        }
        close();
      }} noValidate>
      <div className={styles.periodFields}>
        <label>من تاريخ<input ref={dateRef} type="date" aria-label={`من تاريخ (${name})`} value={draft.from}
          min="2000-01-01" max="2100-12-31" aria-invalid={!!error} aria-describedby={error ? `${id}-error` : undefined} onChange={event => setDraft({...draft, from: event.target.value})}/></label>
        <label>إلى تاريخ<input type="date" aria-label={`إلى تاريخ (${name})`} value={draft.to}
          min="2000-01-01" max="2100-12-31" aria-invalid={!!error} aria-describedby={error ? `${id}-error` : undefined} onChange={event => setDraft({...draft, to: event.target.value})}/></label>
      </div>
      {error && <p id={`${id}-error`} className={styles.periodError} role="alert">{error}</p>}
      <div className={styles.periodActions}>
        <button type="submit">تطبيق الفترة</button>
        <button type="button" onClick={close}>إلغاء</button>
      </div>
    </form>}
  </div>;
}

function demoBranchPhoto(academy: string, branch: string): string | undefined {
  if (!academy.toLowerCase().includes("professional")) return;
  if (branch.includes("مدينتي")) return "/demo-assets/branch-madinaty.webp";
  if (branch.includes("الرحاب")) return "/demo-assets/branch-rehab.webp";
  if (branch.includes("الشروق")) return "/demo-assets/branch-shorouk.webp";
}

export function OwnerBranchRail({
  academy, branches, branchId, onChange,
}: {
  academy: string;
  branches: Branch[];
  branchId: string;
  onChange: (id: string) => void;
}) {
  return <section aria-label="الفروع" className={styles.branchSection}>
    <div className={styles.section}><MapPin size={19}/><h2>الفروع</h2>
      {branchId
        ? <button type="button" onClick={() => onChange("")}>كل الفروع</button>
        : <span>كل الفروع</span>}
    </div>
    <div className={styles.branchRail}>
      {branches.map(branch => {
        const photo = demoBranchPhoto(academy, branch.arabicName);
        const active = branch.id === branchId;
        return <button key={branch.id} type="button" aria-pressed={active}
          onClick={() => onChange(active ? "" : branch.id)}>
          {photo ? <Image src={photo} fill sizes="150px" alt=""/> : <MapPin size={25} aria-hidden="true"/>}
          <strong>{branch.arabicName}</strong>
          {active && <Check className={styles.branchCheck} size={18} aria-hidden="true"/>}
        </button>;
      })}
    </div>
  </section>;
}
