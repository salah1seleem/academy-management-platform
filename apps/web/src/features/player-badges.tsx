"use client";

import Link from "next/link";
import { useRef, useState } from "react";
import { Button, EmptyState, ErrorState, LoadingState, PageHeader, SectionCard } from "../components/dashboard-shell";
import { GuardianShell } from "../components/guardian-shell";
import { formatDateAr } from "../lib/formatters";
import { csrfRequest, DashboardRequestError } from "./dashboard-api";
import { useGoalData } from "./player-goals";
import styles from "./player-badges.module.css";

const root = "/api/v1/evaluations/badges";
type Rule = { id: string; sportId: string; title: string; criterion: string; points: number; version: number; retired: boolean };
type Badge = {
  id: string; version: number; title: string; criterion: string; points: number;
  state: "Granted" | "Revoked"; observedOn: string; actorName: string; familyNote: string | null;
  revocation: null | { actorName: string; familyNote: string | null; createdAtUtc: string };
};
type Journey = {
  enrollment: { id: string; playerId: string; player: string; sport: string };
  enrollmentVersion: number; asOfDate: string; canGrant: boolean; canRevoke: boolean;
  points: number; rules: Rule[]; items: Badge[]; hasMore: boolean;
};

function useBadgeCommand(saved: () => void) {
  const [busy, setBusy] = useState(false), [error, setError] = useState(""), [conflict, setConflict] = useState(false);
  const [message, setMessage] = useState("");
  const sending = useRef(false);
  const intent = useRef<{ signature: string; id: string } | null>(null);
  async function save(url: string, body: object, success: string) {
    if (sending.current || conflict) return;
    sending.current = true;
    setBusy(true);
    setError("");
    setMessage("");
    const signature = JSON.stringify({ url, body });
    if (intent.current?.signature !== signature) intent.current = { signature, id: crypto.randomUUID() };
    try {
      await csrfRequest(url, "POST", { ...body, requestId: intent.current.id }, {}, { announce: false });
      intent.current = null;
      setMessage(success);
      saved();
    } catch (failure) {
      if (failure instanceof DashboardRequestError && failure.status === 409) setConflict(true);
      setError(failure instanceof DashboardRequestError ? failure.message : "تعذر تأكيد الحفظ. حدّث السجل للتحقق.");
    } finally {
      sending.current = false;
      setBusy(false);
    }
  }

  function refresh(read: () => void) {
    setConflict(false);
    setError("");
    read();
  }

  return { locked: busy || conflict, error, message, save, refresh };
}

function ReadError({ message, reload, family }: { message: string; reload: () => void; family?: boolean }) {
  return <div className={family ? styles.familyPanel : undefined}>
    <ErrorState message={message} />
    <Button className={family ? styles.familyRetry : undefined} variant="secondary" onClick={reload}>إعادة المحاولة</Button>
  </div>;
}

function OptionalAction({ title, description, button, onSave, busy, family = false }: {
  title: string; description: string; button: string; onSave: (note: string | null) => void; busy: boolean; family?: boolean;
}) {
  return <details className={styles.action}>
    <summary>{title}</summary>
    <form className={`form-shell ${styles.compactForm}`} onSubmit={event => {
      event.preventDefault();
      const note = String(new FormData(event.currentTarget).get("note") ?? "").trim();
      onSave(note || null);
    }}>
      <p>{description}</p>
      <label>{family ? "ملاحظة السحب للأسرة (اختياري)" : "ملاحظة (اختياري)"}<textarea name="note" maxLength={500} disabled={busy} /></label>
      <Button variant="danger" type="submit" disabled={busy}>{button}</Button>
    </form>
  </details>;
}

export function BadgeRules() {
  const [revision, setRevision] = useState(0);
  const { data, error, reload } = useGoalData<{ sports: { id: string; name: string }[]; items: Rule[]; canCreate: boolean; canRetire: boolean; hasMore: boolean }>(
    `${root}/rules`, revision, "تعذر تحميل قواعد الشارات.",
  );
  const command = useBadgeCommand(() => setRevision(n => n + 1));
  return <>
    <PageHeader title="قواعد شارات المجهود" context="الفريق والتطور"
      description="تحدد كل قاعدة متى تُمنح الشارة في رياضة واحدة. النقاط للتشجيع." />
    {error && <ReadError message={error} reload={reload} />}
    {command.error && <div><ErrorState message={command.error} /><Button variant="secondary" onClick={() => command.refresh(reload)}>تحديث القواعد</Button></div>}
    {command.message && <p role="status">{command.message}</p>}
    {!data ? !error && <LoadingState /> : <>
      {data.canCreate && data.sports.length > 0 && <details className="detail-card action-disclosure">
        <summary>إضافة قاعدة تشجيع</summary>
        <form className={`form-shell ${styles.compactForm}`} onSubmit={event => {
          event.preventDefault();
          const fields = new FormData(event.currentTarget);
          void command.save(`${root}/rules`, {
            sportId: fields.get("sport"), title: fields.get("title"), criterion: fields.get("criterion"),
            points: Number(fields.get("points")), confirmPublication: true,
          }, "نُشرت قاعدة الشارة.");
        }}>
          <label>الرياضة<select name="sport" required disabled={command.locked}><option value="">اختر الرياضة</option>
            {data.sports.map(sport => <option key={sport.id} value={sport.id}>{sport.name}</option>)}
          </select></label>
          <label>اسم الشارة<input name="title" required maxLength={100} disabled={command.locked} /></label>
          <label>متى يستحقها اللاعب؟<textarea name="criterion" required maxLength={500} disabled={command.locked} /></label>
          <label>نقاط تشجيعية غير مالية<input name="points" type="number" min={0} max={1000} required defaultValue={10} disabled={command.locked} /></label>
          <p>بعد النشر، تبقى القاعدة ثابتة وتصبح متاحة للمنح.</p>
          <Button type="submit" disabled={command.locked}>حفظ ونشر قاعدة الشارة</Button>
        </form>
      </details>}
      <div className={styles.grid}>{data.items.map(rule => <SectionCard key={rule.id} title={rule.title}>
        <div className={styles.metadata}>
          <span className={rule.retired ? styles.inactive : styles.active}>{rule.retired ? "موقوفة للمنح الجديد" : "متاحة للمنح"}</span>
          <span>{data.sports.find(sport => sport.id === rule.sportId)?.name}</span>
        </div>
        <p>{rule.criterion}</p>
        <p>{rule.points} نقطة تشجيعية</p>
        {data.canRetire && !rule.retired && <OptionalAction title="إيقاف القاعدة مع حفظ السابق"
          description="يوقف هذا الإجراء المنح الجديد من هذه القاعدة، وتبقى الشارات السابقة في سجل اللاعبين."
          button="تأكيد إيقاف قاعدة الشارة" busy={command.locked}
          onSave={note => void command.save(`${root}/rules/${rule.id}/retire`, { version: rule.version, note }, "توقفت القاعدة عن المنح الجديد.")} />}
      </SectionCard>)}</div>
      {!data.items.length && <EmptyState message="لا توجد قواعد منشورة للشارات." />}
      {data.hasMore && <p>تظهر أحدث 100 قاعدة.</p>}
    </>}
  </>;
}

type GrantDraft = { enrollment: string; rule: string; date: string; note: string };
export function PlayerBadges({ enrollmentId, playerId }: { enrollmentId?: string; playerId?: string }) {
  const family = Boolean(playerId);
  const profile = `/guardian/children/${playerId}`;
  const scoped = family && Boolean(enrollmentId);
  const query = scoped ? `?${new URLSearchParams({ enrollmentId: enrollmentId! })}` : "";
  const [revision, setRevision] = useState(0);
  const [draft, setDraft] = useState<GrantDraft>();
  const { data, error, reload } = useGoalData<{ enrollments: Journey[]; hasMore: boolean }>(
    family ? `/api/v1${profile}/badges${query}` : `${root}/enrollments/${enrollmentId}`, revision, "تعذر تحميل سجل الشارات.",
  );
  const command = useBadgeCommand(() => {
    setDraft(undefined);
    setRevision(n => n + 1);
  });
  const journeys = data?.enrollments.filter(journey => !scoped || journey.enrollment.id === enrollmentId) ?? [];
  const staffProfile = journeys[0]?.enrollment.playerId;

  const body = <div className={family ? styles.family : undefined}>
    {family ? <header className={`guardian-page-header guardian-match-header ${styles.familyHeader}`}>
      <Link className="guardian-match-back" href={profile}>العودة إلى ملف اللاعب</Link>
      <h1>شارات المجهود والتحسن</h1>
    </header> : <PageHeader title="شارات المجهود والتحسن" context="التدريب والتطور"
      back={staffProfile ? { label: "العودة إلى ملف اللاعب", href: `/dashboard/players/${staffProfile}?tab=development` } : undefined}
      secondaryActions={[{ label: "قواعد الشارات", href: "/dashboard/evaluations/badges/rules" }]} />}
    {scoped && <Link className={`guardian-card-cta ${styles.allSports}`} href={`${profile}/badges`}>عرض شارات كل الرياضات</Link>}
    {error && <ReadError message={error} reload={reload} family={family} />}
    {command.error && <div><ErrorState message={command.error} /><Button variant="secondary" onClick={() => command.refresh(reload)}>تحديث السجل</Button></div>}
    {command.message && <p role="status">{command.message}</p>}
    {!data ? !error && <LoadingState /> : <>
      {!journeys.length && <div className={family ? styles.familyPanel : undefined}>
        <EmptyState message={scoped ? "سجل هذا التسجيل الرياضي غير متاح." : "لا يوجد تسجيل رياضي لعرض شاراته."} />
      </div>}
      {journeys.map(journey => {
        const enrollment = journey.enrollment;
        const fields = draft?.enrollment === enrollment.id ? draft : { enrollment: enrollment.id, rule: "", date: journey.asOfDate, note: "" };
        const rule = journey.rules.find(item => item.id === fields.rule);
        return <section key={enrollment.id} className={styles.journey}>
          <SectionCard title={`${enrollment.player} · ${enrollment.sport}`}>
            <div className={styles.balance}><strong>{journey.points}</strong><span>نقاط الشارات السارية في هذه الرياضة</span></div>
          </SectionCard>
          {!family && journey.canGrant && journey.rules.length > 0 && <form className={`form-shell ${styles.compactForm}`} onSubmit={event => {
            event.preventDefault();
            if (!rule) return;
            void command.save(`${root}/enrollments/${enrollment.id}/grant`, {
              ruleId: rule.id, enrollmentVersion: journey.enrollmentVersion,
              observedOn: fields.date, familyNote: fields.note.trim() || null,
            }, "مُنحت الشارة وأصبحت متاحة للأسرة.");
          }}>
            <label>قاعدة الشارة<select name="rule" value={rule?.id ?? ""} required disabled={command.locked}
              onChange={event => setDraft({ ...fields, rule: event.target.value })}>
              <option value="">اختر شارة متاحة</option>
              {journey.rules.map(item => <option key={item.id} value={item.id}>{item.title} · {item.points} نقطة</option>)}
            </select></label>
            {rule && <p>{rule.criterion}</p>}
            <label>تاريخ الملاحظة<input name="date" type="date" required min="1900-01-01" max={journey.asOfDate} value={fields.date}
              onChange={event => setDraft({ ...fields, date: event.target.value })} disabled={command.locked} /></label>
            <label>ملاحظة للأسرة (اختياري)<textarea name="note" maxLength={500} value={fields.note}
              onChange={event => setDraft({ ...fields, note: event.target.value })} disabled={command.locked} /></label>
            <p>تُمنح كل شارة مرة واحدة لهذا التسجيل الرياضي. الملاحظة تظهر للأسرة.</p>
            <Button type="submit" disabled={command.locked || !rule}>منح الشارة ونشرها للأسرة</Button>
          </form>}
          {!family && journey.canGrant && journey.rules.length === 0 && <p className={styles.hint}>لا توجد شارات متاحة للمنح لهذا التسجيل.</p>}
          {journey.items.length > 0 ? <div className={styles.grid}>{journey.items.map(badge => {
            const active = badge.state === "Granted";
            return <SectionCard key={badge.id} title={badge.title}>
              <div className={styles.metadata}>
                <span className={active ? styles.active : styles.inactive}>{active ? "شارة سارية" : "شارة مسحوبة"}</span>
                <span>{badge.points} نقطة{!active && " عند المنح"}</span>
              </div>
              <p>{badge.criterion}</p>
              <p>تاريخ الملاحظة {formatDateAr(badge.observedOn)} · منحها {badge.actorName}</p>
              {badge.familyNote && <p>{badge.familyNote}</p>}
              {badge.revocation && <div className={styles.revoked}>
                <strong>سحبها {badge.revocation.actorName} · {formatDateAr(badge.revocation.createdAtUtc.slice(0, 10))}</strong>
                {badge.revocation.familyNote && <p>{badge.revocation.familyNote}</p>}
              </div>}
              {!family && journey.canRevoke && active && <OptionalAction family title="سحب الشارة مع حفظ سجلها"
                description="تُستبعد نقاط الشارة من الإجمالي، ويظل السحب ظاهرًا في سجل الأسرة."
                button="تأكيد سحب الشارة" busy={command.locked}
                onSave={note => void command.save(`${root}/grants/${badge.id}/revoke`, { version: badge.version, note }, "سُحبت الشارة وحُدّث إجمالي النقاط.")} />}
            </SectionCard>;
          })}</div> : <div className={family ? styles.familyPanel : undefined}><EmptyState message="لا توجد شارات مسجلة لهذه الرياضة." /></div>}
          {journey.hasMore && <p>تظهر أحدث 100 شارة. يشمل الإجمالي كل الشارات السارية في هذه الرياضة.</p>}
        </section>;
      })}
      {!scoped && data.hasMore && <p>تظهر أول 20 تسجيلًا رياضيًا.</p>}
      {family && journeys.length > 0 && <details className={styles.familyPanel}>
        <summary>عن نقاط الشارات</summary>
        <p>النقاط للتشجيع. تُستبعد الشارات المسحوبة من الإجمالي، ويظل السحب ظاهرًا في السجل.</p>
      </details>}
    </>}
  </div>;

  return family ? <GuardianShell>{body}</GuardianShell> : body;
}
