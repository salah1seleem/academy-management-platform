"use client";

import { useEffect, useRef, useState } from "react";
import { ConfirmationDialog, ErrorState, LoadingState } from "../components/dashboard-shell";
import { formatDateAr } from "../lib/formatters";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";

type ReferenceStatus = { revision: string; expiresAtUtc: string | null };

function referenceStatus(value: unknown): ReferenceStatus {
  if (!value || typeof value !== "object" || !("revision" in value) || !("expiresAtUtc" in value) ||
      typeof value.revision !== "string" || !/^(none|[a-f0-9]{32})$/.test(value.revision) ||
      (value.expiresAtUtc !== null && (typeof value.expiresAtUtc !== "string" || !Number.isFinite(Date.parse(value.expiresAtUtc))))) {
    throw new Error("Invalid reference status");
  }
  return { revision: value.revision, expiresAtUtc: value.expiresAtUtc };
}

export function BeneficiaryRenewalCode({ enrollmentId, player, sport, branch, onClose }: {
  enrollmentId: string; player: string; sport: string; branch: string; onClose: () => void;
}) {
  const path = `/api/v1/subscriptions/beneficiary-references/${enrollmentId}`;
  const panel = useRef<HTMLElement>(null);
  const output = useRef<HTMLInputElement>(null);
  const inFlight = useRef<AbortController | null>(null);
  const [status, setStatus] = useState<ReferenceStatus | null>(null);
  const [readVersion, setReadVersion] = useState(0);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [confirm, setConfirm] = useState(false);
  const [code, setCode] = useState("");
  const [copyMessage, setCopyMessage] = useState("");

  useEffect(() => { panel.current?.focus(); return () => inFlight.current?.abort(); }, []);
  useEffect(() => { if (code) output.current?.focus(); }, [code]);
  useEffect(() => {
    const controller = new AbortController();
    void (async () => {
      try {
        const response = await fetch(path, { cache: "no-store", signal: controller.signal });
        if (!response.ok) throw new DashboardRequestError("", response.status);
        const current = referenceStatus(await response.json());
        if (!controller.signal.aborted) setStatus(current);
      } catch (caught) {
        if (controller.signal.aborted) return;
        const unavailable = caught instanceof DashboardRequestError && [401, 403, 404].includes(caught.status);
        setError(unavailable ? "كود التجديد غير متاح لهذا التسجيل أو لصلاحيتك الحالية." : "تعذر قراءة حالة الكود. أعد التحقق.");
      }
    })();
    return () => controller.abort();
  }, [path, readVersion]);

  async function issue() {
    if (!status || inFlight.current || code) return;
    const controller = new AbortController();
    inFlight.current = controller;
    setBusy(true);
    setError("");
    try {
      const response = await csrfRequest(`${path}/regenerate`, "POST", {}, { "If-Match": `"${status.revision}"` }, { announce: false, signal: controller.signal });
      const data = await response.json();
      const saved = referenceStatus(data);
      if (typeof data.reference !== "string" || !/^RNW-[A-F0-9]{24}$/.test(data.reference) || !saved.expiresAtUtc || saved.revision === status.revision) {
        throw new Error("Invalid issuance response");
      }
      if (!controller.signal.aborted) {
        setStatus(saved);
        setCode(data.reference);
      }
    } catch (caught) {
      if (controller.signal.aborted) return;
      setStatus(null);
      setError(caught instanceof DashboardRequestError && caught.status === 412
        ? "تغيّرت حالة الكود في مكان آخر. تحقّق من الحالة قبل المتابعة."
        : caught instanceof DashboardRequestError && [401, 403, 404].includes(caught.status)
          ? "كود التجديد غير متاح لهذا التسجيل أو لصلاحيتك الحالية."
          : "تعذر تأكيد إصدار الكود. تحقّق من الحالة قبل إعادة المحاولة.");
    } finally {
      inFlight.current = null;
      if (!controller.signal.aborted) { setBusy(false); setConfirm(false); }
    }
  }

  async function copyCode() {
    try {
      await navigator.clipboard.writeText(code);
      setCopyMessage("تم نسخ الكود.");
    } catch {
      output.current?.focus();
      output.current?.select();
      setCopyMessage("تعذر النسخ تلقائيًا. انسخ الكود المحدد.");
    }
  }

  return <section ref={panel} tabIndex={-1} id="beneficiary-renewal-code" className="detail-card renewal-code-panel" aria-labelledby="beneficiary-renewal-code-title">
    <h3 id="beneficiary-renewal-code-title">كود التجديد للغير</h3>
    <p><strong>{player}</strong> · {sport} · {branch}</p>
    {error && <ErrorState message={error} />}
    {!status && !error && <LoadingState message="جارٍ قراءة حالة الكود…" />}
    {status && <p role="status">{status.expiresAtUtc ? `الكود سارٍ حتى ${formatDateAr(status.expiresAtUtc)}` : "لا يوجد كود سارٍ."}</p>}
    {code && <><label className="field">الكود الجديد<input ref={output} dir="ltr" type="text" value={code} readOnly autoComplete="off" spellCheck={false} /></label>
      <p>انسخ الكود قبل الإغلاق. يظهر النص الكامل عند الإصدار فقط.</p></>}
    {copyMessage && <p role="status">{copyMessage}</p>}
    <div className="form-actions">
      {code && <button type="button" className="button button-primary" onClick={() => void copyCode()}>نسخ الكود</button>}
      {status && !code && <button type="button" className="button button-primary" disabled={busy} onClick={() => status.expiresAtUtc ? setConfirm(true) : void issue()}>
        {busy ? "جارٍ إصدار الكود…" : status.expiresAtUtc ? "استبدال الكود" : "إصدار كود"}
      </button>}
      {error && <button type="button" className="button button-secondary" onClick={() => { setError(""); setReadVersion(value => value + 1); }}>تحقّق من الحالة</button>}
      <button type="button" className="button button-secondary" disabled={busy} onClick={onClose}>إغلاق</button>
    </div>
    <ConfirmationDialog open={confirm} title="استبدال كود التجديد؟" message="بمجرد إصدار الكود الجديد، يتوقف الكود السابق عن العمل." confirmLabel="إصدار كود جديد" busy={busy}
      busyLabel="جارٍ إصدار الكود…" onConfirm={() => void issue()} onCancel={() => setConfirm(false)} />
  </section>;
}
