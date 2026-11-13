"use client";

import { useEffect, useRef, useState } from "react";
import { Button, ErrorState } from "../components/dashboard-shell";
import { formatDateAr } from "../lib/formatters";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { useGoalData } from "./player-goals";
import { PlayerSessionChange } from "./player-session-change";
import { PlayerCompensation } from "./player-compensation";
import { TransferSchedule, type TransferSlot } from "./transfer-schedule";
import styles from "./player-skills.module.css";

type Group = { id: string; arabicName: string; sportId: string; branchId: string; registrationFormat?: string };
type Preview = { fingerprint: string; effectiveDate: string; sourceGroup: string; sourceGroupId: string; targetGroup: string; targetGroupId: string; conflicts: string[]; removedSessions: TransferSlot[]; addedSessions: TransferSlot[] };
type Transfer = { id: string; effectiveDate: string; sourceGroupId: string; targetGroupId: string };
type Attempt = { preview: Preview; key: string; receipt?: string; acknowledged?: boolean };
type Props = { enrollmentId: string; playerId: string; groupId: string; onSaved?: () => void };

function oneYearAfter(day: string) {
  const [year, month, date] = day.split("-").map(Number);
  const lastDay = new Date(Date.UTC(year + 1, month, 0)).getUTCDate();
  return `${year + 1}-${day.slice(5, 7)}-${String(Math.min(date, lastDay)).padStart(2, "0")}`;
}

export function PlayerGroupChange(props: Props) {
  return <GroupChange key={`${props.enrollmentId}:${props.groupId}`} {...props} />;
}

function GroupChange({ enrollmentId, playerId, groupId, onSaved }: Props) {
  const path = `/api/v1/people/enrollments/${enrollmentId}/group-transfer`;
  const options = useGoalData<{ groups: Group[] }>("/api/v1/manage/structure/options", 0, "تعذر تحميل المجموعات.");
  const calendar = useGoalData<{ today: string }>("/api/v1/academy/calendar", 0, "تعذر تحميل تاريخ الأكاديمية.");
  const choices = Array.isArray(options.data?.groups) ? options.data.groups : null;
  const source = choices?.find(item => item.id === groupId);
  const groups = choices?.filter(item => source && item.sportId === source.sportId &&
    item.branchId === source.branchId && (item.registrationFormat ?? "Group") === (source.registrationFormat ?? "Group")) ?? [];
  const [selected, setSelected] = useState("");
  const [pickedDate, setPickedDate] = useState<string | null>(null);
  const [review, setReview] = useState<{ preview: Preview; options: typeof options.data; calendar: typeof calendar.data } | null>(null);
  const [operation, setOperation] = useState<"preview" | "save" | "check" | null>(null);
  const [needsCheck, setNeedsCheck] = useState<Attempt | null>(null);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [completed, setCompleted] = useState(false);
  const inFlight = useRef(false);
  const intent = useRef<{ signature: string; key: string } | null>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const academyDate = calendar.data?.today ?? "";
  const today = /^\d{4}-\d{2}-\d{2}$/.test(academyDate) ? academyDate : "";
  const date = pickedDate ?? today ?? "";
  const max = today ? oneYearAfter(today) : "";
  const target = groups.find(item => item.id === selected && item.id !== groupId);
  const targets = groups.filter(item => item.id !== groupId);
  const canReview = Boolean(target && today && date >= today && date <= max);
  const preview = review?.options === options.data && review?.calendar === calendar.data ? review?.preview : null;
  const locked = operation !== null || needsCheck !== null;
  const loadError = options.error || calendar.error ||
    (options.data && !choices ? "تعذر تحميل المجموعات." : "") ||
    (calendar.data && !today ? "تعذر تحميل تاريخ الأكاديمية." : "");

  useEffect(() => {
    if (preview) heading.current?.focus();
  }, [preview]);

  function invalidate() {
    setReview(null);
    setError("");
    setMessage("");
    setCompleted(false);
  }

  async function reviewTransfer() {
    if (!canReview || locked || inFlight.current || !target) return;
    inFlight.current = true;
    setOperation("preview");
    invalidate();
    try {
      const response = await csrfRequest(`${path}/preview`, "POST", { groupId: target.id, effectiveDate: date }, {}, { announce: false });
      const result = await response.json() as Preview;
      if (result.targetGroupId !== target.id || result.effectiveDate !== date || !result.sourceGroupId) {
        setError("تغيّرت بيانات النقل. أعد المعاينة.");
        return;
      }
      setReview({ preview: result, options: options.data, calendar: calendar.data });
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "تعذر معاينة النقل.");
    } finally {
      inFlight.current = false;
      setOperation(null);
    }
  }

  async function readTransfer(attempt: Attempt) {
    const response = await fetch(`${path}s`, { cache: "no-store" });
    if (!response.ok) throw new Error("تعذر قراءة سجل النقل. أعد التحقق.");
    const records = await response.json() as Transfer[];
    const reviewed = attempt.preview;
    const found = records.some(record =>
      (!attempt.receipt || record.id === attempt.receipt) &&
      record.effectiveDate === reviewed.effectiveDate &&
      record.sourceGroupId === reviewed.sourceGroupId &&
      record.targetGroupId === reviewed.targetGroupId,
    );
    setReview(null);
    if (!found && attempt.acknowledged) {
      setNeedsCheck(attempt);
      setError("لم يظهر سجل النقل بعد. تحقّق من السجل مرة أخرى.");
      return;
    }
    setNeedsCheck(null);
    if (found) {
      intent.current = null;
      setCompleted(true);
      setMessage(`تم تسجيل نقل المجموعة إلى ${reviewed.targetGroup} من ${formatDateAr(reviewed.effectiveDate)}.`);
      onSaved?.();
    } else {
      setMessage("لا يوجد نقل مسجّل بهذا التاريخ. أعد المعاينة قبل التأكيد.");
    }
  }

  async function checkTransfer() {
    if (!needsCheck || inFlight.current) return;
    inFlight.current = true;
    setOperation("check");
    setError("");
    try {
      await readTransfer(needsCheck);
    } catch {
      setError("تعذر قراءة سجل النقل. أعد التحقق.");
    } finally {
      inFlight.current = false;
      setOperation(null);
    }
  }

  async function save() {
    if (!preview?.fingerprint || preview.conflicts.length || locked || inFlight.current) return;
    const body = { groupId: preview.targetGroupId, effectiveDate: preview.effectiveDate, fingerprint: preview.fingerprint };
    const signature = JSON.stringify(body);
    if (intent.current?.signature !== signature) intent.current = { signature, key: crypto.randomUUID() };
    let attempt: Attempt = { preview, key: intent.current.key };
    inFlight.current = true;
    setOperation("save");
    setError("");
    try {
      const response = await csrfRequest(path, "POST", body, { "Idempotency-Key": attempt.key }, { announce: false });
      attempt = { ...attempt, acknowledged: true };
      const receipt = await response.json().catch(() => null) as { id?: string } | null;
      attempt = { ...attempt, receipt: receipt?.id };
      await readTransfer(attempt);
    } catch (caught) {
      const status = caught instanceof DashboardRequestError ? caught.status : 0;
      setReview(null);
      if (attempt.acknowledged || status === 0 || status >= 500 || status === 409) {
        setNeedsCheck(attempt);
        setError(status === 409 && caught instanceof DashboardRequestError
          ? caught.message
          : "تعذر تأكيد النقل. تحقّق من سجل النقل قبل إعادة المحاولة.");
      } else {
        setError(caught instanceof Error ? caught.message : "تعذر حفظ النقل.");
      }
    } finally {
      inFlight.current = false;
      setOperation(null);
    }
  }

  return <>
    <details className="placement-level">
      <summary>تعديل المجموعة</summary>
      <div className={styles.groupTransferForm}>
        {message && options.data && today && <p role="status">{message}</p>}
        {completed && options.data && today && <Button variant="secondary" onClick={() => {
          setSelected("");
          setPickedDate(null);
          invalidate();
          options.reload();
          calendar.reload();
        }}>نقل آخر</Button>}
        {error && <ErrorState message={error} />}
        {needsCheck && <Button variant="secondary" disabled={operation !== null} onClick={() => void checkTransfer()}>
          {operation === "check" ? "جارٍ قراءة سجل النقل…" : "تحقّق من سجل النقل"}
        </Button>}
        {loadError ? <><ErrorState message={loadError} /><Button variant="secondary" onClick={() => { options.reload(); calendar.reload(); }}>إعادة تحميل الخيارات</Button></>
          : (!options.data || !today) ? <p role="status">جارٍ تحميل خيارات النقل…</p>
          : !completed && (targets.length ? <>
            <div className={`form-grid ${styles.groupTransferInputs}`}>
              <label>المجموعة الجديدة<select aria-label="تعديل المجموعة" disabled={locked} value={target?.id ?? ""}
                onChange={event => { setSelected(event.target.value); invalidate(); }}>
                <option value="">اختر المجموعة</option>
                {targets.map(item => <option key={item.id} value={item.id}>{item.arabicName}</option>)}
              </select></label>
              <label>بدء النقل<input type="date" min={today} max={max} value={date} disabled={locked}
                onChange={event => { setPickedDate(event.target.value); invalidate(); }} /></label>
            </div>
            {date && (date < today || date > max) && <p>اختر تاريخًا من اليوم وحتى سنة بتوقيت الأكاديمية.</p>}
            <Button variant="secondary" disabled={locked || !canReview} onClick={() => void reviewTransfer()}>
              {operation === "preview" ? "جارٍ تحميل المعاينة…" : "معاينة نقل المجموعة"}
            </Button>
          </> : <p>لا توجد مجموعة بديلة مطابقة في هذا الفرع.</p>)}
        {preview && <section className="section-card" aria-label="معاينة النقل">
          <header><h3 ref={heading} tabIndex={-1}>أثر النقل على الجدول</h3></header>
          <dl className={styles.placementFacts}>
            <div><dt>المجموعة الحالية في تاريخ النقل</dt><dd>{preview.sourceGroup}</dd></div>
            <div><dt>المجموعة الجديدة</dt><dd>{preview.targetGroup}</dd></div>
            <div><dt>بداية النقل</dt><dd>{formatDateAr(preview.effectiveDate)}</dd></div>
          </dl>
          {preview.conflicts.length > 0 && <div className="state-message error-message" role="alert"><ul>
            {preview.conflicts.map(conflict => <li key={conflict}>{conflict}</li>)}
          </ul></div>}
          <TransferSchedule title="مواعيد تخرج من جدول اللاعب" sessions={preview.removedSessions} />
          <TransferSchedule title="مواعيد تضاف إلى جدول اللاعب" sessions={preview.addedSessions} />
          <p>يتغيّر جدول اللاعب من تاريخ النقل، مع حفظ الاشتراك والتحصيل وسجل الحضور.</p>
          <Button disabled={locked || preview.conflicts.length > 0} onClick={() => void save()}>
            {operation === "save" ? "جارٍ تأكيد النقل…" : "تأكيد نقل المجموعة"}
          </Button>
        </section>}
      </div>
    </details>
    <PlayerSessionChange playerId={playerId} enrollmentId={enrollmentId} groups={groups} onSaved={onSaved} />
    <PlayerCompensation enrollmentId={enrollmentId} onSaved={onSaved} />
  </>;
}
