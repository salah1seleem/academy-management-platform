"use client";

import { useEffect, useRef, useState } from "react";
import { CalendarCheck, RotateCcw } from "lucide-react";
import { Button, ConfirmationDialog, ErrorState } from "../components/dashboard-shell";
import { formatDateAr, formatSessionDateAr, formatTimeAr } from "../lib/formatters";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import styles from "./player-skills.module.css";

type Booking = {
  id: string; trainingSessionId?: string; group: string; date: string;
  units: number; consumed: number; state: string; version: number; startTime?: string; endTime?: string;
};
export type Compensation = {
  id: string; sportEnrollmentId?: string; sport: string; cause: string; unit: string; units: number;
  consumed: number; allocated: number; validFrom: string; validTo: string;
  sourceDate: string; sourceSessionId?: string; bookings: Booking[];
};
type Source = { id: string; group: string; date: string };
type Slot = Source & { start: string; end: string };
type Options = { today: string; sources: Source[]; targets: Slot[] };
type Review = { fingerprint: string; units: number; conflicts: string[]; available?: number; expectedPlayers?: number; capacity?: number | null; target?: Slot };
const causes = {
  CancelledSession: "إلغاء الحصة",
  CoachAbsent: "غياب المدربين",
  ApprovedAbsence: "غياب اللاعب بموافقة الإدارة",
};
function amount(value: number, unit: string) {
  const minutes = unit === "Minute";
  const singular = minutes ? "دقيقة" : "حصة";
  const plural = minutes ? "دقائق" : "حصص";
  if (value === 1) return `${singular} واحدة`;
  if (value === 2) return minutes ? "دقيقتان" : "حصتان";
  return `${value} ${value <= 10 ? plural : singular}`;
}
const remaining = (grant: Compensation) => Math.max(0, grant.units - grant.allocated);
const slotTime = (slot: Slot) => `من ${formatTimeAr(slot.start)} إلى ${formatTimeAr(slot.end)}`;
const addDays = (date: string, days: number) => new Date(Date.parse(`${date}T12:00:00Z`) + days * 86400000).toISOString().slice(0, 10);

type GrantCommand = { sourceSessionId: string; cause: string; unit: string; validFrom: string; validTo: string };
type Command =
  | { kind: "grant"; path: string; body: GrantCommand; source: Source; unit: string }
  | { kind: "book"; path: string; body: { trainingSessionId: string }; grantId: string; slot: Slot; unit: string }
  | { kind: "cancel"; path: string; body: { version: number }; booking: Booking };
type Reviewed = { command: Exclude<Command, { kind: "cancel" }>; data: Review; key: string };
type SavedCheck = { command: Command; units?: number; id?: string; acknowledged: boolean };

function matchSaved({ command, units, id }: SavedCheck, grants: Compensation[]) {
  if (command.kind === "cancel") {
    return grants.some(grant => grant.bookings.some(booking => booking.id === command.booking.id && booking.state === "Cancelled"));
  }
  if (command.kind === "book") {
    return grants.some(grant => grant.id === command.grantId && grant.bookings.some(booking =>
      (!id || booking.id === id) && booking.trainingSessionId === command.body.trainingSessionId &&
      booking.units === units && booking.state === "Reserved"));
  }
  const body = command.body;
  return grants.some(grant => (!id || grant.id === id) && grant.sourceSessionId === body.sourceSessionId &&
    grant.cause === body.cause && grant.unit === body.unit && grant.units === units &&
    grant.validFrom === body.validFrom && grant.validTo === body.validTo);
}

function bookingStatus(booking: Booking, today?: string) {
  if (booking.state === "Cancelled") return "أُلغي الحجز";
  if (booking.consumed > 0) return "تم الحضور بالتعويض";
  return today && booking.date < today ? "انتهى الموعد" : "موعد محجوز";
}

export function CompensationCards({ items, onRelease, releaseDisabled, family, today }: {
  items?: Compensation[];
  onRelease?: (booking: Booking) => void;
  releaseDisabled?: boolean;
  family?: boolean;
  today?: string;
}) {
  if (!items?.length) return null;
  return <section className={`compensation-cards ${styles.compensationCards}`} aria-label="التعويضات المعتمدة">
    <h3><CalendarCheck size={18} aria-hidden="true" /> التعويضات المعتمدة</h3>
    {items.map(grant => <article key={grant.id}>
      <strong>{grant.sport} · {amount(grant.units, grant.unit)}</strong>
      <small>عن حصة {formatDateAr(grant.sourceDate)} · صالح حتى {formatDateAr(grant.validTo)}</small>
      {family && today && today < grant.validFrom
        ? <small>يبدأ الحجز من {formatDateAr(grant.validFrom)}</small>
        : family && today && today > grant.validTo
          ? <small>انتهت صلاحية الرصيد</small>
          : <small>{family && today ? "متاح للحجز" : "غير المحجوز"} {amount(remaining(grant), grant.unit)}</small>}
      {grant.consumed > 0 && <small>المستخدم في الحضور {amount(grant.consumed, grant.unit)}</small>}
      {grant.bookings.filter(booking => booking.state === "Reserved" || (family || onRelease) && booking.state === "Cancelled").map(booking => <div className={styles.compensationBooking} key={booking.id}>
        <div>
          <small>{booking.group} · {formatSessionDateAr(booking.date)}</small>
          {family && booking.startTime && booking.endTime && <small>من {formatTimeAr(booking.startTime)} إلى {formatTimeAr(booking.endTime)}</small>}
          <small className={styles.compensationBookingStatus}>{!family && booking.state === "Cancelled" ? "حجز محرر" : bookingStatus(booking, today)}</small>
        </div>
        {onRelease && booking.state === "Reserved" && booking.consumed === 0 &&
          <Button variant="secondary" disabled={releaseDisabled} onClick={() => onRelease(booking)}>تحرير حجز التعويض</Button>}
      </div>)}
    </article>)}
  </section>;
}

export function PlayerCompensation(props: { enrollmentId: string; onSaved?: () => void }) {
  return <CompensationPanel key={props.enrollmentId} {...props} />;
}

function CompensationPanel({ enrollmentId, onSaved }: { enrollmentId: string; onSaved?: () => void }) {
  const root = `/api/v1/people/enrollments/${enrollmentId}`;
  const [open, setOpen] = useState(false);
  const [options, setOptions] = useState<Options>();
  const [items, setItems] = useState<Compensation[]>([]);
  const [loading, setLoading] = useState(false);
  const [loadError, setLoadError] = useState("");
  const [source, setSource] = useState("");
  const [cause, setCause] = useState("CancelledSession");
  const [unit, setUnit] = useState("Session");
  const [from, setFrom] = useState("");
  const [to, setTo] = useState("");
  const [grant, setGrant] = useState("");
  const [target, setTarget] = useState("");
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [review, setReview] = useState<Reviewed>();
  const [cancel, setCancel] = useState<Booking>();
  const [pending, setPending] = useState<SavedCheck>();
  const previewRef = useRef<HTMLElement>(null);
  const loadRef = useRef<AbortController | null>(null);
  const intent = useRef<{ signature: string; key: string } | null>(null);

  useEffect(() => () => loadRef.current?.abort(), []);
  useEffect(() => { if (review) previewRef.current?.focus(); }, [review]);

  function availableGrants(grants: Compensation[], today: string) {
    return grants.filter(item => remaining(item) > 0 && item.validTo >= today);
  }

  async function readGrants(signal?: AbortSignal) {
    const response = await fetch(`${root}/compensations`, { cache: "no-store", signal });
    if (!response.ok) throw new Error();
    const data: unknown = await response.json();
    if (!Array.isArray(data)) throw new Error();
    return data as Compensation[];
  }

  async function load() {
    loadRef.current?.abort();
    const controller = new AbortController();
    loadRef.current = controller;
    setLoading(true);
    setLoadError("");
    setOptions(undefined);
    setReview(undefined);
    try {
      const [response, grants] = await Promise.all([
        fetch(`${root}/compensation/options`, { cache: "no-store", signal: controller.signal }),
        readGrants(controller.signal),
      ]);
      if (!response.ok) throw new Error();
      const next = await response.json() as Options;
      const defaultEnd = addDays(next.today, 30);
      if (controller.signal.aborted) return;
      setOptions(next);
      setItems(grants);
      setFrom(previous => previous >= next.today ? previous : next.today);
      setTo(previous => previous >= next.today ? previous : defaultEnd);
      setSource(previous => next.sources.some(item => item.id === previous) ? previous : "");
      setGrant(previous => {
        const available = availableGrants(grants, next.today);
        return available.some(item => item.id === previous) ? previous : available[0]?.id ?? "";
      });
      setTarget("");
    } catch {
      if (!controller.signal.aborted) setLoadError("تعذر تحميل التعويضات والمواعيد. أعد المحاولة.");
    } finally {
      if (!controller.signal.aborted) setLoading(false);
    }
  }

  function toggle(next: boolean) {
    setOpen(next);
    if (next && !busy && !pending) void load();
    if (!next) {
      loadRef.current?.abort();
      setLoading(false);
      setCancel(undefined);
      if (!busy && !pending) setReview(undefined);
    }
  }

  function invalidate() {
    setReview(undefined);
    setError("");
    setMessage("");
  }

  async function preview(command: Reviewed["command"]) {
    setBusy(true);
    invalidate();
    try {
      const response = await csrfRequest(`${command.path}/preview`, "POST", command.body);
      const data = await response.json() as Review;
      if (!data.fingerprint || !Array.isArray(data.conflicts)) throw new Error();
      if (command.kind === "book" && data.target && data.target.id !== command.slot.id) {
        throw new DashboardRequestError("تغيّر موعد الحجز. أعد المعاينة.", 409);
      }
      const signature = JSON.stringify([command.path, command.body]);
      if (intent.current?.signature !== signature) intent.current = { signature, key: crypto.randomUUID() };
      const reviewedCommand = command.kind === "book" && data.target ? { ...command, slot: data.target } : command;
      setReview({ command: reviewedCommand, data, key: intent.current.key });
    } catch (failure) {
      setError(failure instanceof DashboardRequestError ? failure.message : "تعذر عرض المعاينة. أعد المحاولة.");
    } finally {
      setBusy(false);
    }
  }

  async function verify(check: SavedCheck) {
    let grants: Compensation[];
    try {
      grants = await readGrants();
    } catch {
      setPending(check);
      setError("تعذرت قراءة سجل التعويض. حاول التحقق مرة أخرى.");
      return;
    }
    setItems(grants);
    if (!matchSaved(check, grants)) {
      if (check.acknowledged) {
        setPending(check);
        setError("النتيجة لم تظهر في السجل بعد. تحقّق مرة أخرى.");
      } else {
        setPending(undefined);
        setReview(undefined);
        setError(check.command.kind === "cancel"
          ? "الحجز ما زال ظاهرًا. راجع السجل قبل تحريره من جديد."
          : "لم تظهر نتيجة مطابقة في السجل. أعد المعاينة قبل التأكيد.");
        await load();
      }
      return;
    }
    setReview(undefined);
    setPending(undefined);
    setError("");
    setMessage({
      grant: "تم اعتماد التعويض دون تحصيل أو تمديد اشتراك.",
      book: "تم حجز الحصة التعويضية وتحديث مواعيد ولي الأمر.",
      cancel: "تم تحرير الحجز مع حفظ سجله.",
    }[check.command.kind]);
    if (check.command.kind === "grant") setSource("");
    setTarget("");
    await load();
    onSaved?.();
  }

  async function save(command: Command, reviewed?: Reviewed) {
    setBusy(true);
    setCancel(undefined);
    setError("");
    setMessage("");
    const check: SavedCheck = { command, units: reviewed?.data.units, acknowledged: false };
    try {
      const body = reviewed ? { ...command.body, fingerprint: reviewed.data.fingerprint } : command.body;
      const headers: Record<string, string> = reviewed ? { "Idempotency-Key": reviewed.key } : {};
      const response = await csrfRequest(command.path, "POST", body, headers, { announce: false });
      const receipt = await response.json().catch(() => null) as { id?: string } | null;
      await verify({ ...check, acknowledged: true, id: receipt?.id });
    } catch (failure) {
      if (!(failure instanceof DashboardRequestError) || failure.status >= 500) {
        setPending(check);
        setError("تعذر تأكيد الحفظ. تحقّق من سجل التعويض أولًا.");
      } else {
        setReview(undefined);
        setError(failure.message);
        if (failure.status === 409) await load();
      }
    } finally {
      setBusy(false);
    }
  }

  const grants = options ? availableGrants(items, options.today) : [];
  const selectedGrant = grants.find(item => item.id === grant);
  const slots = options?.targets.filter(slot => selectedGrant &&
    slot.date >= selectedGrant.validFrom && slot.date <= selectedGrant.validTo &&
    !selectedGrant.bookings.some(booking => booking.trainingSessionId === slot.id && booking.state === "Reserved")) ?? [];
  const selectedSlot = slots.find(slot => slot.id === target);
  const selectedSource = options?.sources.find(item => item.id === source);
  const disabled = busy || !!pending;
  const latestDate = from ? addDays(from, 90) : undefined;
  const validDates = !!options && from >= options.today && !!to && to >= from && !!latestDate && to <= latestDate;

  return <details className="placement-level" onToggle={event => toggle(event.currentTarget.open)}>
    <summary><RotateCcw size={17} aria-hidden="true" /> التعويضات والحصص البديلة</summary>
    {open && <div className={styles.compensationPanel}>
      {loading && <p role="status">جارٍ تحميل التعويضات…</p>}
      {loadError && <div><ErrorState message={loadError} /><Button variant="secondary" disabled={disabled} onClick={() => void load()}>إعادة تحميل التعويضات</Button></div>}
      {options && <>
        <CompensationCards items={items} onRelease={setCancel} releaseDisabled={disabled} />
        {!items.length && <p className="muted">لا توجد تعويضات معتمدة لهذا التسجيل.</p>}
        {options.sources.length ? <fieldset disabled={disabled}>
          <legend>اعتماد تعويض</legend>
          <div className={`form-grid ${styles.compensationInputs}`}>
            <label>الحصة الأصلية<select aria-label="الحصة الأصلية" value={source} onChange={event => { setSource(event.target.value); invalidate(); }}>
              <option value="">اختر الحصة</option>
              {options.sources.map(slot => <option key={slot.id} value={slot.id}>{slot.group} · {formatSessionDateAr(slot.date)}</option>)}
            </select></label>
            <label>سبب التعويض<select value={cause} onChange={event => { setCause(event.target.value); invalidate(); }}>
              {Object.entries(causes).map(([id, label]) => <option key={id} value={id}>{label}</option>)}
            </select></label>
            <label>وحدة التعويض<select value={unit} onChange={event => { setUnit(event.target.value); invalidate(); }}>
              <option value="Session">حصة واحدة</option><option value="Minute">مدة الحصة بالدقائق</option>
            </select></label>
            <label>صالح من<input type="date" min={options.today} value={from} onChange={event => { setFrom(event.target.value); invalidate(); }} /></label>
            <label>صالح إلى<input type="date" min={from} max={latestDate} value={to} onChange={event => { setTo(event.target.value); invalidate(); }} /></label>
          </div>
          {!validDates && <p className="muted">اختر صلاحية من تاريخ اليوم ولمدة لا تتجاوز 90 يومًا.</p>}
          <Button variant="secondary" disabled={!selectedSource || !validDates} onClick={() => {
            if (selectedSource) void preview({ kind: "grant", path: `${root}/compensation`, body: { sourceSessionId: source, cause, unit, validFrom: from, validTo: to }, source: selectedSource, unit });
          }}>معاينة استحقاق التعويض</Button>
        </fieldset> : <p className="muted">لا توجد حصة أصلية متاحة للاعتماد.</p>}
        {!!grants.length && <fieldset disabled={disabled}>
          <legend>حجز حصة بالتعويض</legend>
          <div className={`form-grid ${styles.compensationInputs}`}>
            <label>التعويض<select aria-label="التعويض" value={selectedGrant?.id ?? ""} onChange={event => { setGrant(event.target.value); setTarget(""); invalidate(); }}>
              <option value="">اختر التعويض</option>
              {grants.map(item => <option value={item.id} key={item.id}>حصة {formatDateAr(item.sourceDate)} · متاح {amount(remaining(item), item.unit)}</option>)}
            </select></label>
            {selectedGrant && !!slots.length && <label>الحصة التعويضية<select aria-label="الحصة التعويضية" value={target} onChange={event => { setTarget(event.target.value); invalidate(); }}>
              <option value="">اختر موعدًا</option>
              {slots.map(slot => <option key={slot.id} value={slot.id}>{slot.group} · {formatSessionDateAr(slot.date)} · {slotTime(slot)}</option>)}
            </select></label>}
          </div>
          {selectedGrant && !slots.length && <p className="muted">لا توجد حصة متاحة خلال صلاحية هذا التعويض.</p>}
          <Button variant="secondary" disabled={!selectedGrant || !selectedSlot} onClick={() => {
            if (selectedGrant && selectedSlot) void preview({ kind: "book", path: `/api/v1/people/compensations/${selectedGrant.id}/booking`, body: { trainingSessionId: target }, grantId: selectedGrant.id, slot: selectedSlot, unit: selectedGrant.unit });
          }}>معاينة حجز التعويض</Button>
        </fieldset>}
        {!!items.length && !grants.length && <p className="muted">لا يوجد رصيد تعويض صالح للحجز الآن.</p>}
      </>}
      {review && <section ref={previewRef} tabIndex={-1} className={`section-card ${styles.compensationReview}`} aria-label="معاينة التعويض">
        <h3>{review.command.kind === "grant" ? "مراجعة الاستحقاق" : "مراجعة الحصة التعويضية"}</h3>
        <dl className={styles.placementFacts}>
          {review.command.kind === "grant" ? <>
            <div><dt>الحصة الأصلية</dt><dd>{review.command.source.group} · {formatSessionDateAr(review.command.source.date)}</dd></div>
            <div><dt>السبب</dt><dd>{causes[review.command.body.cause as keyof typeof causes]}</dd></div>
            <div><dt>الصلاحية</dt><dd>من {formatDateAr(review.command.body.validFrom)} إلى {formatDateAr(review.command.body.validTo)}</dd></div>
            <div><dt>التعويض</dt><dd>{amount(review.data.units, review.command.unit)}</dd></div>
          </> : <>
            <div><dt>المجموعة</dt><dd>{review.command.slot.group}</dd></div>
            <div><dt>الموعد</dt><dd>{formatSessionDateAr(review.command.slot.date)} · {slotTime(review.command.slot)}</dd></div>
            <div><dt>يُحجز من التعويض</dt><dd>{amount(review.data.units, review.command.unit)}</dd></div>
            {review.data.available != null && <div><dt>المتاح قبل الحجز</dt><dd>{amount(review.data.available, review.command.unit)}</dd></div>}
            {review.data.expectedPlayers != null && <div><dt>العدد بعد الحجز</dt><dd>{review.data.expectedPlayers}{review.data.capacity != null ? ` من ${review.data.capacity}` : ""}</dd></div>}
          </>}
        </dl>
        {review.command.kind === "grant" && <p className="muted">يُضاف التعويض دون تحصيل أو تمديد الاشتراك.</p>}
        {!!review.data.conflicts.length && <ul className={styles.compensationConflicts} role="alert">
          {review.data.conflicts.map(conflict => <li key={conflict}>{conflict}</li>)}
        </ul>}
        {!pending && <Button disabled={busy || !!review.data.conflicts.length} onClick={() => void save(review.command, review)}>
          {review.command.kind === "grant" ? "تأكيد استحقاق التعويض" : "تأكيد حجز التعويض"}
        </Button>}
      </section>}
      {error && <ErrorState message={error} />}
      {pending && <Button variant="secondary" disabled={busy} onClick={async () => {
        setBusy(true);
        try { await verify(pending); } finally { setBusy(false); }
      }}>تحقّق من سجل التعويض</Button>}
      {message && <p role="status">{message}</p>}
      <ConfirmationDialog open={!!cancel} title="تحرير حجز التعويض"
        message={cancel ? `${cancel.group} · ${formatSessionDateAr(cancel.date)}. تُعاد الوحدات إلى الرصيد ويبقى الحجز في السجل.` : ""}
        confirmLabel="تأكيد تحرير الحجز" onCancel={() => setCancel(undefined)} onConfirm={() => {
          if (cancel) void save({ kind: "cancel", path: `/api/v1/people/compensation-bookings/${cancel.id}/cancel`, body: { version: cancel.version }, booking: cancel });
        }} />
    </div>}
  </details>;
}
