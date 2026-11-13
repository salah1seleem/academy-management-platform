"use client";

import { useEffect, useRef, useState } from "react";
import { CalendarClock } from "lucide-react";
import { Button, ErrorState } from "../components/dashboard-shell";
import { formatDateAr, formatTimeAr } from "../lib/formatters";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import styles from "./player-skills.module.css";

type Group = { id: string; arabicName: string };
type Slot = { id: string; group: string; date: string; start: string; end: string };
type Pair = { sourceSessionId: string; targetSessionId: string };
type Move = Pair & { batchId: string };
type Options = { original: Slot[]; alternative: Slot[] };
type Change = { source: Slot; target: Slot; expectedPlayers: number; capacity: number | null };
type Preview = { fingerprint: string; conflicts: string[]; changes: Change[] };
type Attempt = { pairs: Pair[]; receipt?: string; acknowledged?: boolean };

const when = (slot: Slot) => `${formatDateAr(slot.date)} · من ${formatTimeAr(slot.start)} إلى ${formatTimeAr(slot.end)}`;
const samePairs = (expected: Pair[], actual: Pair[]) => expected.length === actual.length &&
  expected.every(pair => actual.some(move => move.sourceSessionId === pair.sourceSessionId && move.targetSessionId === pair.targetSessionId));

export function PlayerSessionChange({ enrollmentId, playerId, groups, onSaved }: {
  enrollmentId: string; playerId: string; groups: Group[]; onSaved?: () => void;
}) {
  const path = `/api/v1/people/enrollments/${enrollmentId}/session-transfer`;
  const [open, setOpen] = useState(false);
  const [target, setTarget] = useState("");
  const [revision, setRevision] = useState(0);
  const [result, setResult] = useState<{ query: string; data: Options | null; error: string } | null>(null);
  const [selection, setSelection] = useState<Record<string, string>>({});
  const [reviewed, setPreview] = useState<{ preview: Preview; basis: Options } | null>(null);
  const [operation, setOperation] = useState<"preview" | "save" | "check" | null>(null);
  const [needsCheck, setNeedsCheck] = useState<Attempt | null>(null);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const inFlight = useRef(false);
  const intent = useRef<{ signature: string; key: string } | null>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const group = groups.find(item => item.id === target);
  const url = `${path}/options${group ? `?targetGroup=${encodeURIComponent(group.id)}` : ""}`;
  const query = `${url}:${revision}`;
  const options = open && result?.query === query ? result.data : null;
  const loadError = open && result?.query === query ? result.error : "";
  const preview = reviewed && reviewed.basis === options ? reviewed.preview : null;
  const pairs = Object.entries(selection).map(([sourceSessionId, targetSessionId]) => ({ sourceSessionId, targetSessionId }));
  const stale = options && pairs.some(pair =>
    !options.original.some(slot => slot.id === pair.sourceSessionId) ||
    (pair.targetSessionId && !options.alternative.some(slot => slot.id === pair.targetSessionId)));
  const locked = operation !== null || needsCheck !== null;

  useEffect(() => {
    if (!open) return;
    const controller = new AbortController();
    const { signal } = controller;
    fetch(url, { signal, cache: "no-store" }).then(async response => {
      if (!response.ok) throw new Error();
      const data = await response.json() as Options;
      if (!Array.isArray(data.original) || !Array.isArray(data.alternative)) throw new Error();
      if (!signal.aborted) setResult({ query, data, error: "" });
    }).catch(() => {
      if (!signal.aborted) setResult({ query, data: null, error: "تعذر تحميل الحصص المتاحة." });
    });
    return () => controller.abort();
  }, [open, query, url]);

  useEffect(() => {
    if (!open) return;
    const refresh = () => setRevision(n => n + 1);
    window.addEventListener("focus", refresh);
    return () => window.removeEventListener("focus", refresh);
  }, [open]);

  useEffect(() => {
    if (preview && options) heading.current?.focus();
  }, [preview, options]);

  function invalidate() {
    setPreview(null);
    setMessage("");
    setError("");
  }

  async function review() {
    if (!options || stale || locked || inFlight.current || !pairs.length || pairs.some(pair => !pair.targetSessionId)) return;
    inFlight.current = true;
    setOperation("preview");
    invalidate();
    try {
      const response = await csrfRequest(`${path}/preview`, "POST", { pairs }, {}, { announce: false });
      const value = await response.json() as Preview;
      const reviewed = value.changes.map(({ source, target }) => ({ sourceSessionId: source.id, targetSessionId: target.id }));
      if (!samePairs(pairs, reviewed)) throw new Error("تغيّرت الحصص المتاحة. حدّث الخيارات وأعد المعاينة.");
      setPreview({ preview: value, basis: options });
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : "تعذر معاينة الحصص.");
    } finally {
      inFlight.current = false;
      setOperation(null);
    }
  }

  async function readTransfer(attempt: Attempt) {
    const response = await fetch(`/api/v1/people/players/${playerId}`, { cache: "no-store" });
    if (!response.ok) throw new Error();
    const player = await response.json() as { enrollments: { id: string; sessionMoves: Move[] }[] };
    const enrollment = player.enrollments.find(item => item.id === enrollmentId);
    if (!enrollment || !Array.isArray(enrollment.sessionMoves)) throw new Error();
    const batches = new Map<string, Move[]>();
    for (const row of enrollment.sessionMoves) {
      if (!row.batchId || (attempt.receipt && attempt.receipt !== row.batchId)) continue;
      const batch = batches.get(row.batchId) ?? [];
      batch.push(row);
      batches.set(row.batchId, batch);
    }
    const saved = [...batches.values()].some(batch => samePairs(attempt.pairs, batch));
    setPreview(null);
    if (!saved && attempt.acknowledged) {
      setNeedsCheck(attempt);
      setError("لم تظهر الحصص في سجل النقل بعد. أعد التحقق.");
      return;
    }
    setNeedsCheck(null);
    setRevision(n => n + 1);
    if (saved) {
      intent.current = null;
      setSelection({});
      setMessage("تم تسجيل نقل الحصص المختارة.");
      onSaved?.();
    } else {
      setMessage("لم يظهر نقل مطابق في السجل. أعد المعاينة قبل التأكيد.");
    }
  }

  async function check() {
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
    if (!preview?.fingerprint || preview.conflicts.length || stale || !options || locked || inFlight.current) return;
    const body = { pairs, fingerprint: preview.fingerprint };
    const signature = JSON.stringify(body);
    if (intent.current?.signature !== signature) intent.current = { signature, key: crypto.randomUUID() };
    let attempt: Attempt = { pairs };
    inFlight.current = true;
    setOperation("save");
    setError("");
    try {
      const response = await csrfRequest(path, "POST", body, { "Idempotency-Key": intent.current.key }, { announce: false });
      attempt = { ...attempt, acknowledged: true };
      const receipt = await response.json().catch(() => null) as { id?: string } | null;
      attempt = { ...attempt, receipt: receipt?.id };
      await readTransfer(attempt);
    } catch (caught) {
      const status = caught instanceof DashboardRequestError ? caught.status : 0;
      setPreview(null);
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

  function reset() {
    setSelection({});
    invalidate();
  }

  return <details className="placement-level" onToggle={event => {
    setOpen(event.currentTarget.open);
    if (event.currentTarget.open) {
      setRevision(n => n + 1);
      setPreview(null);
    }
  }}>
    <summary><CalendarClock size={17} aria-hidden="true" /> نقل حصص مختارة</summary>
    {open && <div className={`${styles.groupTransferForm} ${styles.sessionTransferForm}`}>
      {message && <p role="status">{message}</p>}
      {error && <ErrorState message={error} />}
      {needsCheck && <Button variant="secondary" disabled={operation !== null} onClick={() => void check()}>
        {operation === "check" ? "جارٍ قراءة سجل النقل…" : "تحقّق من سجل النقل"}
      </Button>}
      {!needsCheck && <>
        <div className={`form-grid ${styles.groupTransferInputs}`}>
          <label>مجموعة الحصة البديلة<select aria-label="مجموعة الحصة البديلة" value={group?.id ?? ""} disabled={locked} onChange={event => {
            setTarget(event.target.value);
            reset();
          }}>
            <option value="">اختر المجموعة</option>
            {groups.map(item => <option key={item.id} value={item.id}>{item.arabicName}</option>)}
          </select></label>
        </div>
        {loadError ? <>
          <ErrorState message={loadError} />
          <Button variant="secondary" onClick={() => { setPreview(null); setRevision(n => n + 1); }}>إعادة تحميل الحصص</Button>
        </> : !options ? <p role="status">جارٍ تحميل الحصص…</p>
          : !options.original.length ? <p>لا توجد حصص قابلة للنقل لهذا التسجيل.</p>
          : !group ? <p>اختر مجموعة لعرض المواعيد البديلة.</p>
          : !options.alternative.length ? <p>لا توجد حصص بديلة في المجموعة المختارة.</p>
          : <>
            {stale && <><ErrorState message="تغيّرت الحصص المتاحة. أعد اختيار الحصص وبدائلها." /><Button variant="secondary" onClick={reset}>مسح الاختيارات</Button></>}
            <div className="session-move-list">{options.original.map(slot => <div className="session-move-row" key={slot.id}>
              <label className="session-move-select">
                <input type="checkbox" checked={Object.hasOwn(selection, slot.id)} disabled={locked} onChange={event => {
                  const checked = event.target.checked;
                  setSelection(current => {
                    const next = { ...current };
                    if (checked) next[slot.id] = "";
                    else delete next[slot.id];
                    return next;
                  });
                  invalidate();
                }} />
                <span><strong>{slot.group}</strong><small>{when(slot)}</small></span>
              </label>
              {Object.hasOwn(selection, slot.id) && <label>بديل حصة {formatDateAr(slot.date)}
                <select aria-label={`بديل حصة ${when(slot)}`} value={selection[slot.id]} disabled={locked} onChange={event => {
                  setSelection(current => ({ ...current, [slot.id]: event.target.value }));
                  invalidate();
                }}>
                  <option value="">اختر موعدًا بديلًا</option>
                  {options.alternative.map(item => <option key={item.id} value={item.id}
                    disabled={pairs.some(pair => pair.sourceSessionId !== slot.id && pair.targetSessionId === item.id)}>{when(item)}</option>)}
                </select>
              </label>}
            </div>)}</div>
            <Button variant="secondary" disabled={locked || Boolean(stale) || !pairs.length || pairs.some(pair => !pair.targetSessionId)} onClick={() => void review()}>
              {operation === "preview" ? "جارٍ تحميل المعاينة…" : "معاينة نقل الحصص"}
            </Button>
          </>}
      </>}
      {preview && options && <section className="section-card" aria-label="معاينة نقل الحصص">
        <header><h3 ref={heading} tabIndex={-1}>أثر النقل على الجدول</h3></header>
        {preview.conflicts.length > 0 && <div className="state-message error-message" role="alert"><ul>
          {preview.conflicts.map(conflict => <li key={conflict}>{conflict}</li>)}
        </ul></div>}
        {preview.changes.map(({ source, target, expectedPlayers, capacity }) => <dl className={styles.placementFacts} key={source.id}>
          <div><dt>الحصة الأصلية</dt><dd>{source.group}<br />{when(source)}</dd></div>
          <div><dt>الحصة البديلة</dt><dd>{target.group}<br />{when(target)}</dd></div>
          <div><dt>العدد بعد النقل</dt><dd>{expectedPlayers} لاعب{capacity !== null ? ` · السعة ${capacity}` : ""}</dd></div>
        </dl>)}
        <p>يتغيّر موعد الحصص المختارة، مع حفظ المجموعة الأساسية والاشتراك وسجل الحضور.</p>
        <Button disabled={locked || preview.conflicts.length > 0} onClick={() => void save()}>
          {operation === "save" ? "جارٍ تأكيد النقل…" : "تأكيد نقل الحصص"}
        </Button>
      </section>}
    </div>}
  </details>;
}
