"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { createPortal, flushSync } from "react-dom";
import { Award } from "lucide-react";
import { Button, EmptyState, ErrorState, LoadingState, PageHeader, SectionCard } from "../components/dashboard-shell";
import { GuardianShell } from "../components/guardian-shell";
import { formatDateAr } from "../lib/formatters";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { useGoalData } from "./player-goals";
import styles from "./player-certificates.module.css";

type Certificate = {
  id: string; sourceLevelReviewId: string; academyName: string; playerName: string; sportName: string;
  levelName: string; milestoneOn: string; issuedOn: string; actorName: string; number: string; version: number;
  state: "Issued" | "Withdrawn";
  withdrawal: null | { actorName: string; familyNote: string | null; createdAtUtc: string };
};
type Data = {
  certificate: Certificate | null; canWithdraw?: boolean; canIssue?: boolean; eligible?: boolean;
  source?: { id: string; version: number; toLevelName: string; player: string; sport: string; academy: string; createdAtUtc: string };
};
const root = "/api/v1/evaluations/certificates";

function CertificatePaper({ certificate }: { certificate: Certificate }) {
  const withdrawn = certificate.state === "Withdrawn";
  return <article className={styles.paper} aria-label="شهادة إنجاز الأكاديمية">
    {withdrawn && <div role="status" className={styles.withdrawn}>
      <strong>الشهادة مسحوبة</strong>
      {certificate.withdrawal && <>
        <p>سحبها {certificate.withdrawal.actorName} · {formatDateAr(certificate.withdrawal.createdAtUtc.slice(0, 10))}</p>
        {certificate.withdrawal.familyNote && <p>{certificate.withdrawal.familyNote}</p>}
      </>}
    </div>}
    <div className={styles.medal}><Award size={38} aria-hidden /></div>
    <p className={styles.academy}>{certificate.academyName}</p>
    <h2>شهادة إنجاز فني</h2>
    <h3>{certificate.playerName}</h3>
    <p>{certificate.sportName}</p>
    <strong className={styles.level}>{certificate.levelName}</strong>
    <p>تاريخ الإنجاز {formatDateAr(certificate.milestoneOn)}</p>
    <p>أصدرها {certificate.actorName} · {formatDateAr(certificate.issuedOn)}</p>
    <p className={styles.disclaimer}>شهادة داخلية من الأكاديمية، وليست اعتمادًا خارجيًا.</p>
    <p>مرجع الشهادة <bdi>{certificate.number}</bdi></p>
    {!withdrawn && <small>النسخة المطبوعة تعرض حالة الشهادة وقت الطباعة.</small>}
  </article>;
}

function FamilyHeader({ title, back, label }: { title: string; back: string; label: string }) {
  return <header className={`guardian-page-header guardian-match-header ${styles.familyHeader}`}>
    <Link className="guardian-match-back" href={back}>{label}</Link>
    <h1>{title}</h1>
  </header>;
}

function CertificateError({ message, reload }: { message: string; reload: () => void }) {
  return <section className={styles.errorPanel}>
    <ErrorState message={message} />
    <Button variant="secondary" onClick={reload}>إعادة المحاولة</Button>
  </section>;
}

export function PlayerCertificate({ sourceId, id, playerId }: { sourceId?: string; id?: string; playerId?: string }) {
  const [revision, setRevision] = useState(0);
  const [busy, setBusy] = useState<"print" | "write">();
  const [conflict, setConflict] = useState(false);
  const [failure, setFailure] = useState("");
  const [message, setMessage] = useState("");
  const [printable, setPrintable] = useState<{ path: string; certificate: Certificate }>();
  const intent = useRef<{ body: string; id: string } | null>(null);
  const printing = useRef<AbortController | null>(null);
  const writing = useRef(false);
  const profile = `/guardian/children/${playerId}`;
  const path = playerId ? `/api/v1${profile}/certificates/${id}` : sourceId ? `${root}/sources/${sourceId}` : `${root}/${id}`;
  const { data, error, reload } = useGoalData<Data>(path, revision, "تعذر تحميل الشهادة أو مصدرها.");
  const certificate = data?.certificate;
  const source = data?.source;
  const locked = Boolean(busy || conflict);

  useEffect(() => {
    const clearPrint = () => setPrintable(undefined);
    const onVisibility = () => {
      if (document.hidden) {
        printing.current?.abort();
        clearPrint();
      }
    };
    window.addEventListener("afterprint", clearPrint);
    window.addEventListener("focus", clearPrint);
    document.addEventListener("visibilitychange", onVisibility);
    return () => {
      printing.current?.abort();
      window.removeEventListener("afterprint", clearPrint);
      window.removeEventListener("focus", clearPrint);
      document.removeEventListener("visibilitychange", onVisibility);
    };
  }, [path]);

  async function write(url: string, body: object, success: string) {
    if (writing.current || printing.current || conflict || playerId) return;
    writing.current = true;
    setBusy("write");
    setFailure("");
    setMessage("");
    const signature = JSON.stringify({ url, body });
    if (intent.current?.body !== signature) intent.current = { body: signature, id: crypto.randomUUID() };
    try {
      await csrfRequest(url, "POST", { ...body, requestId: intent.current.id }, {}, { announce: false });
      intent.current = null;
      setMessage(success);
      setRevision(value => value + 1);
    } catch (caught) {
      if (caught instanceof DashboardRequestError && caught.status === 409) setConflict(true);
      setFailure(caught instanceof DashboardRequestError ? caught.message : "تعذر تأكيد الإجراء. حدّث سجل الشهادة للتحقق.");
    } finally {
      writing.current = false;
      setBusy(undefined);
    }
  }

  async function printCurrent() {
    if (printing.current || writing.current || conflict || certificate?.state !== "Issued") return;
    const controller = new AbortController();
    const { signal } = controller;
    printing.current = controller;
    setBusy("print");
    setFailure("");
    setPrintable(undefined);
    try {
      const response = await fetch(path, { cache: "no-store", signal });
      if (signal.aborted) return;
      if (!response.ok) {
        if ([401, 403, 404].includes(response.status)) reload();
        throw new Error();
      }
      const current = (await response.json() as Data).certificate;
      if (signal.aborted || document.hidden) return;
      if (!current || current.id !== certificate.id || current.state !== "Issued") {
        reload();
        setFailure("تعذرت الطباعة. راجع حالة الشهادة الحالية.");
        return;
      }
      flushSync(() => setPrintable({ path, certificate: current }));
      window.print();
    } catch {
      if (!signal.aborted) {
        setPrintable(undefined);
        setFailure("تعذرت الطباعة. أعد المحاولة للتحقق من حالة الشهادة.");
      }
    } finally {
      printing.current = null;
      setBusy(undefined);
    }
  }

  const body = <div className={`academy-certificate-surface ${playerId ? styles.family : ""}`}>
    {playerId ? <FamilyHeader title="شهادة الإنجاز" back={`${profile}/certificates`} label="العودة إلى شهادات اللاعب" />
      : <PageHeader title={certificate ? "شهادة إنجاز اللاعب" : "مصدر شهادة الإنجاز"} context="التدريب والتطور"
        back={certificate && !sourceId ? { label: "العودة إلى مصدر الإنجاز", href: `/dashboard/evaluations/certificates/sources/${certificate.sourceLevelReviewId}` } : undefined} />}
    {error && <CertificateError message={error} reload={reload} />}
    {failure && <section className={styles.errorPanel}>
      <ErrorState message={failure} />
      {!playerId && <Button variant="secondary" onClick={() => { setConflict(false); setFailure(""); reload(); }}>تحديث سجل الشهادة</Button>}
    </section>}
    {message && <p role="status">{message}</p>}
    {!data ? !error && <LoadingState /> : certificate ? <>
      {certificate.state === "Issued" && <div className={styles.actions}>
        <Button disabled={locked} onClick={() => void printCurrent()}>{busy === "print" ? "جارٍ التحقق من الشهادة…" : "طباعة النسخة الحالية"}</Button>
      </div>}
      <CertificatePaper certificate={certificate} />
      {!playerId && data.canWithdraw && certificate.state === "Issued" && <details className={styles.withdrawAction}>
        <summary>سحب الشهادة مع حفظ سجلها</summary>
        <form className="form-shell" onSubmit={event => {
          event.preventDefault();
          const familyNote = String(new FormData(event.currentTarget).get("note") ?? "").trim() || null;
          void write(`${root}/${certificate.id}/withdraw`, { version: certificate.version, familyNote }, "سُحبت الشهادة. تظهر حالتها الجديدة في سجل الأسرة.");
        }}>
          <p>سيظهر السحب في سجل الأسرة. تبقى النسخ التي سبق طباعتها لدى أصحابها.</p>
          <label>ملاحظة السحب للأسرة (اختياري)<textarea name="note" maxLength={500} disabled={locked} /></label>
          <Button variant="danger" type="submit" disabled={locked}>تأكيد سحب الشهادة</Button>
        </form>
      </details>}
    </> : source ? <SectionCard title="بيانات الإنجاز قبل الإصدار">
      <p><strong>{source.player}</strong> · {source.sport}</p>
      <p>المستوى {source.toLevelName}</p>
      <p>تاريخ تسجيل الإنجاز {formatDateAr(source.createdAtUtc.slice(0, 10))}</p>
      {!playerId && data.canIssue && sourceId ? <>
        <p>بعد الإصدار، تظهر الشهادة في سجل ولي الأمر.</p>
        <Button disabled={locked} onClick={() => void write(`${root}/sources/${sourceId}/issue`, { sourceVersion: source.version }, "صدرت الشهادة وأصبحت متاحة لولي الأمر.")}>إصدار شهادة الإنجاز لولي الأمر</Button>
      </> : <p>{data.eligible ? "إصدار الشهادة يحتاج صلاحية الإضافة." : "هذا السجل غير مؤهل لإصدار شهادة إنجاز."}</p>}
    </SectionCard> : <EmptyState message="لا توجد شهادة لهذا الإنجاز." />}
    <p className={styles.printFallback}>{certificate?.state === "Issued"
      ? "استخدم زر طباعة النسخة الحالية للتحقق من حالة الشهادة."
      : "الطباعة متاحة للشهادات السارية بعد التحقق من حالتها."}</p>
    {printable?.path === path && createPortal(
      <div className={styles.printOutlet} data-certificate-print-ready=""><CertificatePaper certificate={printable.certificate} /></div>,
      document.body,
    )}
  </div>;

  return playerId ? <GuardianShell>{body}</GuardianShell> : body;
}

export function GuardianCertificates({ playerId }: { playerId: string }) {
  const profile = `/guardian/children/${playerId}`;
  const { data, error, reload } = useGoalData<{ items: Certificate[]; hasMore: boolean }>(`/api/v1${profile}/certificates`, 0, "تعذر تحميل الشهادات.");
  return <GuardianShell><div className={styles.family}>
    <FamilyHeader title="شهادات الإنجاز" back={profile} label="العودة إلى ملف اللاعب" />
    {error ? <CertificateError message={error} reload={reload} /> : !data ? <LoadingState /> : <>
      {!data.items.length && <EmptyState message="لا توجد شهادات في سجل اللاعب." />}
      {data.items.map(certificate => <SectionCard key={certificate.id} title={certificate.levelName}>
        <div className={styles.metadata}>
          <span className={certificate.state === "Issued" ? styles.issuedLabel : styles.withdrawnLabel}>
            {certificate.state === "Issued" ? "شهادة صادرة" : "شهادة مسحوبة"}
          </span>
          <span>{certificate.sportName}</span>
        </div>
        <p>{certificate.playerName} · {formatDateAr(certificate.milestoneOn)}</p>
        <Link className="button button-secondary" href={`${profile}/certificates/${certificate.id}`}>فتح الشهادة</Link>
      </SectionCard>)}
      {data.hasMore && <p>تظهر أحدث 50 شهادة.</p>}
    </>}
  </div></GuardianShell>;
}
