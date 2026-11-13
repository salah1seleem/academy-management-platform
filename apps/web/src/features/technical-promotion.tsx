"use client";

import Link from "next/link";
import { useEffect, useRef, useState } from "react";
import { Button, ErrorState, LoadingState, PageHeader, SectionCard } from "../components/dashboard-shell";
import { formatDateAr } from "../lib/formatters";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { useGoalData } from "./player-goals";
import { TransferSchedule, type TransferSlot } from "./transfer-schedule";
import styles from "./player-skills.module.css";

type Options = {
  player?: string;
  sport?: string;
  branch?: string;
  enrollmentVersion: number;
  effectiveDate: string;
  recommendedLevel: string;
  canPreview: boolean;
  applied: null | { id: string; toLevelName: string; createdAtUtc: string; effectiveDate?: string | null };
  groups: { id: string; name: string }[];
  hasMore: boolean;
};
type Preview = {
  fingerprint: string;
  preview: { sourceGroup: string; targetGroup: string; targetGroupId?: string; effectiveDate: string; conflicts: string[]; removedSessions: TransferSlot[]; addedSessions: TransferSlot[] };
  level: string;
  enrollmentVersion: number;
};

export function TechnicalPromotion({ id, reviewQuery = "" }: { id: string; reviewQuery?: string }) {
  const path = `/api/v1/evaluations/technical-reviews/${id}/promotion`;
  const { data, error, reload } = useGoalData<Options>(path, 0, "تعذر تحميل التوصية أو صلاحية التسكين.");
  const [group, setGroup] = useState("");
  const [review, setReview] = useState<{ basis: Options; group: string; result: Preview } | null>(null);
  const [operation, setOperation] = useState<"preview" | "save" | null>(null);
  const [failure, setFailure] = useState("");
  const [needsRead, setNeedsRead] = useState(false);
  const [saved, setSaved] = useState(false);
  const [checked, setChecked] = useState(false);
  const inFlight = useRef(false);
  const pending = useRef<{ signature: string; id: string } | null>(null);
  const heading = useRef<HTMLHeadingElement>(null);
  const selectedGroup = data?.groups.find(item => item.id === group);
  const current = review && data === review.basis && selectedGroup?.id === review.group ? review.result : null;
  const locked = operation !== null || needsRead || saved;

  useEffect(() => {
    if (current) heading.current?.focus();
  }, [current]);

  function check() {
    setReview(null);
    setFailure("");
    if (needsRead && pending.current) setChecked(true);
    setNeedsRead(false);
    reload();
  }

  async function submit(save: boolean) {
    if (!data?.canPreview || data.applied || !selectedGroup || inFlight.current || locked) return;
    if (save && (!current?.fingerprint || current.preview.conflicts.length)) return;
    const body = { groupId: selectedGroup.id, enrollmentVersion: data.enrollmentVersion, fingerprint: save ? current?.fingerprint : null };
    const signature = JSON.stringify(body);
    if (save && pending.current?.signature !== signature) pending.current = { signature, id: crypto.randomUUID() };
    inFlight.current = true;
    setOperation(save ? "save" : "preview");
    setFailure("");
    setChecked(false);
    if (!save) setReview(null);
    try {
      const response = await csrfRequest(path + (save ? "" : "/preview"), "POST", {
        ...body, requestId: save ? pending.current?.id : crypto.randomUUID(),
      }, {}, { announce: false });
      if (save) {
        pending.current = null;
        setSaved(true);
        setReview(null);
        reload();
      } else {
        const result = await response.json() as Preview;
        if (result.enrollmentVersion !== data.enrollmentVersion || result.preview.effectiveDate !== data.effectiveDate ||
          (result.preview.targetGroupId && result.preview.targetGroupId !== selectedGroup.id)) {
          setNeedsRead(true);
          setFailure("تغيّرت بيانات التسكين. حدّثها قبل إعادة المعاينة.");
        } else {
          setReview({ basis: data, group: selectedGroup.id, result });
        }
      }
    } catch (caught) {
      setReview(null);
      const status = caught instanceof DashboardRequestError ? caught.status : 0;
      setNeedsRead(save || [401, 403, 404, 409].includes(status));
      if (save && (status === 0 || status >= 500)) {
        setFailure("تعذر تأكيد التنفيذ. تحقّق من سجل التنفيذ قبل المحاولة مرة أخرى.");
      } else {
        setFailure(caught instanceof DashboardRequestError ? caught.message : "تعذر تحميل المعاينة. أعد المحاولة.");
      }
    } finally {
      setOperation(null);
      inFlight.current = false;
    }
  }

  const back = { label: "العودة إلى الأدلة والقرار", href: `/dashboard/evaluations/technical-reviews/${id}${reviewQuery}` };
  return <>
    <PageHeader title="تنفيذ التوصية الفنية" context="تسكين اللاعب" back={back} />
    {error ? <>
      <ErrorState message={error} /><Button variant="secondary" onClick={check}>إعادة المحاولة</Button>
    </> : !data ? <LoadingState /> : <>
      <SectionCard title={data.player || data.recommendedLevel}>
        {data.player && <p>{[data.sport, data.branch].filter(Boolean).join(" · ")}</p>}
        {data.applied ? <>
          <p role="status">تم التنفيذ إلى <strong>{data.applied.toLevelName}</strong></p>
          <p>{data.applied.effectiveDate ? "بداية النقل" : "تاريخ التسجيل"} · {formatDateAr(data.applied.effectiveDate ?? data.applied.createdAtUtc)}</p>
          <Link className="button button-secondary" href={`/dashboard/evaluations/certificates/sources/${data.applied.id}`}>شهادة إنجاز من هذا المصدر</Link>
        </> : <>
          {data.player && <p>المستوى الموصى به: <strong>{data.recommendedLevel}</strong></p>}
          {failure && <ErrorState message={failure} />}
          {(needsRead || saved) && <Button variant="secondary" onClick={check}>تحقّق من سجل التنفيذ</Button>}
          {saved && <p>لم يظهر سجل التنفيذ بعد. حدّث السجل للتحقق.</p>}
          {checked && <p role="status">لا يوجد تنفيذ مسجّل لهذه التوصية.</p>}
          {!saved && !data.canPreview && <p>تغيّرت الأدلة أو الخطة. يلزم طلب مراجعة فنية جديد.</p>}
          {!saved && data.canPreview && (data.groups.length ? <>
            <div className={styles.curriculumFilter}>
              <label>المجموعة في المستوى الموصى به
                <select value={selectedGroup?.id ?? ""} disabled={locked} onChange={event => {
                  setGroup(event.target.value);
                  setReview(null);
                  setFailure("");
                }}>
                  <option value="">اختر المجموعة</option>
                  {data.groups.map(item => <option key={item.id} value={item.id}>{item.name}</option>)}
                </select>
              </label>
              <Button variant="secondary" disabled={locked || !selectedGroup} onClick={() => void submit(false)}>
                {operation === "preview" ? "جارٍ تحميل المعاينة…" : "معاينة المستوى والمواعيد"}
              </Button>
            </div>
            {data.hasMore && <p className={styles.reviewMeta}>تظهر أول 100 مجموعة مطابقة.</p>}
          </> : <p>لا توجد مجموعة مطابقة في فرع اللاعب. جهّز المجموعة ومواعيدها أولًا.</p>)}
        </>}
      </SectionCard>
      {!data.applied && data.canPreview && current && <section className="section-card" aria-labelledby="promotion-preview">
        <header><h2 id="promotion-preview" ref={heading} tabIndex={-1}>معاينة التسكين</h2></header>
        <dl className={styles.placementFacts}>
          <div><dt>المجموعة الحالية</dt><dd>{current.preview.sourceGroup}</dd></div>
          <div><dt>المجموعة الجديدة</dt><dd>{current.preview.targetGroup}</dd></div>
          <div><dt>المستوى الجديد</dt><dd>{current.level}</dd></div>
          <div><dt>بداية النقل</dt><dd>{formatDateAr(current.preview.effectiveDate)}</dd></div>
        </dl>
        {current.preview.conflicts.length > 0 && <div className="state-message error-message" role="alert">
          <div><strong>تعذّر اعتماد هذه المجموعة</strong><ul>{current.preview.conflicts.map(conflict => <li key={conflict}>{conflict}</li>)}</ul></div>
        </div>}
        <TransferSchedule title="مواعيد تخرج من جدول اللاعب" sessions={current.preview.removedSessions} />
        <TransferSchedule title="مواعيد تضاف إلى جدول اللاعب" sessions={current.preview.addedSessions} />
        {!current.preview.conflicts.length && <p>تُراجع الأهلية عند الاعتماد. يبقى الاشتراك والتحصيل وسجل الحضور محفوظًا.</p>}
        <Button disabled={locked || current.preview.conflicts.length > 0} onClick={() => void submit(true)}>
          {operation === "save" ? "جارٍ اعتماد التسكين…" : "اعتماد المستوى ونقل المجموعة"}
        </Button>
      </section>}
    </>}
  </>;
}
