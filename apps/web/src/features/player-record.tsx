"use client";
import { EnrollmentTransfers, type EnrollmentTransfer } from "./enrollment-transfers";

import Link from "next/link";
import { FormEvent, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { ErrorState, LoadingState, PageHeader, StatusBadge } from "../components/dashboard-shell";
import { IdentityMark } from "../components/sports-ui";
import { csrfRequest, uploadAsset } from "./dashboard-api";
import { SubscriptionPeriodDetail } from "./subscriptions";
import { PlayerGroupChange } from "./player-group-change";
import { PlayerCardPanel } from "./player-card";
import { PlayerRefunds } from "./player-refunds";
import { PlayerDiscounts } from "./discount-rules";
import { formatDateAr, statusLabel } from "../lib/formatters";
import { RecordTabs } from "../components/record-tabs";
import { UnassignedEnrollment } from "./unassigned-enrollment";
import { EnrollmentLevelReview } from "./group-placement";
import { PlacementRecommendations } from "./placement-recommendations";
import { trainingFormatName } from "./group-training-format";

type Enrollment = { transfers?: EnrollmentTransfer[]; sessionMoves?: { date: string; group: string }[]; registrationFormat?: string; id: string; trainingGroupId: string | null; sportId: string; branchId: string; trainingLevelId?: string | null; sportName: string; branchName: string; groupName: string; categoryName: string; status: string; subscription?: { id: string; plan: string; startDate: string; endDate?: string; status: string } };
type Player = { id: string; playerCode: string; arabicName: string; englishName?: string; dateOfBirth: string; gender?: number; heightCm?: number; weightKg?: number; preferredFoot?: number | string; footballPosition?: string; address?: string; isActive: boolean; hasActiveSubscription?: boolean; enrollments: Enrollment[]; photoReference?: string; asOfDate?: string; guardians?: { id: string; displayName: string; relationshipType?: string }[] };

function usePlayer(id: string) { const [player, setPlayer] = useState<Player | null>(null); const [error, setError] = useState(""); const [revision, setRevision] = useState(0); useEffect(() => { fetch(`/api/v1/people/players/${id}`).then(async response => { if (!response.ok) throw new Error(); setPlayer(await response.json() as Player); }).catch(() => setError("تعذر تحميل اللاعب أو أنه خارج نطاق الأكاديمية.")); }, [id, revision]); return { player, error, setError, reload: () => setRevision(v => v + 1) }; }

export function PlayerDetails({ id }: { id: string }) {
  const { player, error, reload } = usePlayer(id);
  const [owner, setOwner] = useState(false);
  useEffect(() => { const abort = new AbortController(); fetch("/api/v1/me", { signal: abort.signal }).then(async r => r.ok ? await r.json() as { role?: string } : {}).then(me => setOwner(me.role === "AcademyOwner")).catch(() => {}); return () => abort.abort(); }, []);
  const birth = player ? new Date(`${player.dateOfBirth}T00:00:00Z`) : null;
  const today = player?.asOfDate ? new Date(`${player.asOfDate}T00:00:00Z`) : new Date();
  const age = birth ? today.getUTCFullYear() - birth.getUTCFullYear() - (today.getUTCMonth() < birth.getUTCMonth() || (today.getUTCMonth() === birth.getUTCMonth() && today.getUTCDate() < birth.getUTCDate()) ? 1 : 0) : null;
  return <><PageHeader title="ملف اللاعب" context="اللاعبون / عرض" action={{ label: "تعديل البيانات", href: `/dashboard/players/${id}/edit` }} />{error ? <ErrorState message={error} /> : !player ? <LoadingState /> : <>
    <section className="athlete-hero" aria-label="هوية اللاعب">
      <div><IdentityMark photo={player.photoReference} name={player.arabicName} detail={player.playerCode} large /><div className="team-chips">{player.enrollments.map(enrollment => <span className="team-chip" key={enrollment.id}>{enrollment.sportName} · {enrollment.branchName} · {enrollment.groupName}</span>)}</div></div>
      <StatusBadge status={player.isActive && (player.hasActiveSubscription ?? player.enrollments.some(e => e.subscription?.status === "Active")) ? "Active" : "Inactive"} />
    </section>
    <RecordTabs label="أقسام ملف اللاعب" tabs={[{ id: "overview", label: "نظرة عامة", content: <><section className="detail-card"><h2>بيانات اللاعب</h2><dl className="detail-grid">
      <div><dt>تاريخ الميلاد</dt><dd>{formatDateAr(player.dateOfBirth)}</dd></div>
      <div><dt>العمر بتاريخ السجل</dt><dd>{age != null && age >= 0 ? `${age} سنوات` : "غير متاح"}</dd></div>
      <div><dt>المركز</dt><dd>{player.footballPosition || "غير مسجل"}</dd></div>
      <div><dt>القدم المفضلة</dt><dd>{({ 1: "اليمنى", 2: "اليسرى", 3: "القدمان", Right: "اليمنى", Left: "اليسرى", Both: "القدمان" } as Record<string, string>)[player.preferredFoot ?? 0] ?? "غير مسجل"}</dd></div>
      <div><dt>الطول / الوزن</dt><dd>{player.heightCm ? `${player.heightCm} سم` : "الطول غير مسجل"} · {player.weightKg ? `${player.weightKg} كجم` : "الوزن غير مسجل"}</dd></div>
    </dl></section>
    <PlayerCardPanel playerId={id} name={player.arabicName} /><section className="detail-card"><h2>المتابعة والتواصل</h2><nav className="profile-record-links" aria-label="سجلات المتابعة"><Link href={`/dashboard/attendance/history?playerId=${player.id}`}>سجل الحضور</Link><Link href={`/dashboard/evaluations?playerId=${player.id}`}>تقييمات اللاعب</Link><Link href={`/dashboard/players/${id}?tab=more#player-guardians`}>إدارة روابط أولياء الأمور</Link></nav></section>
    </> }, { id: "subscriptions", label: "الاشتراكات والمالية", content: <>
    <section className="detail-card"><div className="linked-list"><h3>التسجيلات والاشتراكات</h3>{player.enrollments.length ? player.enrollments.map(enrollment => <article key={enrollment.id}>
      <div><strong>{enrollment.sportName}</strong><small>{enrollment.branchName} · {enrollment.categoryName} · {enrollment.groupName}</small>{enrollment.subscription ? <><small>{enrollment.subscription.plan} · {formatDateAr(enrollment.subscription.startDate)} إلى {enrollment.subscription.endDate ? formatDateAr(enrollment.subscription.endDate) : "بالحصص"}</small><StatusBadge status={enrollment.subscription.status} /></> : <small>لا توجد فترة اشتراك سابقة</small>}</div>
      <div className="enrollment-actions">{owner && enrollment.status === "Active" && <Link href={`/dashboard/finance/free-training/${enrollment.id}`}>اعتماد تدريب مجاني</Link>}<span className="status-pill">{enrollment.trainingGroupId ? statusLabel(enrollment.status) : "في انتظار التسكين"}</span>{enrollment.subscription && <Link href={`/dashboard/subscriptions/periods/${enrollment.subscription.id}`}>سجل الاشتراك</Link>}{enrollment.status === "Active" && enrollment.trainingGroupId && <><Link href={`/dashboard/attendance/sessions?groupId=${enrollment.trainingGroupId}&playerId=${player.id}`}>تسجيل الحضور</Link><Link href={`/dashboard/evaluations/new?enrollmentId=${enrollment.id}`}>تقييم جديد</Link></>}{enrollment.status === "Active" && <Link className="primary-action" href={`/dashboard/subscriptions/renew/new?enrollmentId=${enrollment.id}`}>{enrollment.subscription ? "تجديد الاشتراك" : "تسجيل الاشتراك"}</Link>}</div>
    </article>) : <p>لا توجد تسجيلات رياضية.</p>}</div></section>
    {player.enrollments.map(e => e.subscription && <section key={e.id}><SubscriptionPeriodDetail id={e.subscription.id} embedded /></section>)}
    <PlayerDiscounts playerId={id} /><PlayerRefunds playerId={id} />
    </> }, { id: "training", label: "التدريب والحضور", content: <><UnassignedEnrollment playerId={id} onCreated={reload} />{player.enrollments.map(e => <section className="detail-card" key={e.id}><h2>{e.sportName} · {e.groupName}</h2><p className="team-chip">{trainingFormatName(e.registrationFormat)}</p><p>{e.branchName} · {e.categoryName}</p><EnrollmentLevelReview key={`${e.id}-${e.trainingLevelId}`} enrollmentId={e.id} sportId={e.sportId} levelId={e.trainingLevelId} onSaved={reload} />{e.trainingGroupId ? <><nav className="profile-record-links"><Link href={`/dashboard/academy/groups/${e.trainingGroupId}`}>ملف المجموعة وجدولها</Link><Link href={`/dashboard/attendance/sessions?groupId=${e.trainingGroupId}&playerId=${id}`}>حضور اللاعب</Link></nav><EnrollmentTransfers transfers={e.transfers} moves={e.sessionMoves} /><PlayerGroupChange enrollmentId={e.id} groupId={e.trainingGroupId} onSaved={reload} /></> : <><p className="status-pill">في انتظار التسكين · لا توجد مجموعة مرتبطة</p><PlacementRecommendations enrollmentId={e.id} /></>}</section>)}</> }, { id: "development", label: "التطور", content: <section className="detail-card"><h2>تطور اللاعب</h2><nav className="profile-record-links"><Link href={`/dashboard/evaluations?playerId=${id}`}>جميع تقييمات اللاعب</Link>{player.enrollments.filter(e => e.status === "Active" && e.trainingGroupId).map(e => <Link key={e.id} href={`/dashboard/evaluations/new?enrollmentId=${e.id}`}>تقييم جديد · {e.sportName}</Link>)}</nav></section> }, { id: "more", label: "المزيد", content: <><section className="detail-card" id="player-guardians"><h2>أولياء الأمور المرتبطون</h2>{player.guardians?.length ? <div className="linked-list">{player.guardians.map(guardian => <article key={guardian.id}><IdentityMark name={guardian.displayName} detail={guardian.relationshipType} /><Link href={`/dashboard/guardians/${guardian.id}`}>عرض ولي الأمر</Link></article>)}</div> : <p>لا توجد روابط فعالة مسجلة.</p>}</section><section className="detail-card"><h2>الصحة والوسائط</h2><nav className="profile-record-links"><Link href={`/dashboard/players/medical?playerId=${id}`}>الإصابات والاستشارات</Link><Link href={`/dashboard/players/media?playerId=${id}`}>معرض اللاعب</Link></nav></section></> }]} />
    <Link className="back-link" href="/dashboard/players">العودة إلى قائمة اللاعبين</Link>
  </>}</>;
}

export function PlayerEdit({ id }: { id: string }) {
  const router = useRouter(); const { player, error, setError } = usePlayer(id);
  async function submit(event: FormEvent<HTMLFormElement>) { event.preventDefault(); const data = new FormData(event.currentTarget); try { await csrfRequest(`/api/v1/people/players/${id}`, "PUT", { arabicName: data.get("arabicName"), englishName: data.get("englishName"), dateOfBirth: data.get("dateOfBirth"), gender: Number(data.get("gender")) || null, heightCm: Number(data.get("heightCm")) || null, weightKg: Number(data.get("weightKg")) || null, preferredFoot: Number(data.get("preferredFoot")) || null, footballPosition: data.get("footballPosition"), address: data.get("address") }); const photo = data.get("photo"); if (photo instanceof File && photo.size) await uploadAsset(photo, "Photo", { playerId: id }); router.push(`/dashboard/players/${id}`); } catch (caught) { setError(caught instanceof Error ? caught.message : "تعذر حفظ بيانات اللاعب."); } }
  if (!player) return <><PageHeader title="تعديل اللاعب" context="اللاعبون / تعديل" />{error ? <ErrorState message={error} /> : <LoadingState />}</>;
  return <><PageHeader title="تعديل بيانات اللاعب" context="اللاعبون / تعديل" /><form className="form-shell" onSubmit={event => void submit(event)}><label>الاسم بالعربية<input name="arabicName" defaultValue={player.arabicName} required /></label><label>الاسم بالإنجليزية<input name="englishName" dir="ltr" defaultValue={player.englishName ?? ""} /></label><label>تاريخ الميلاد<input name="dateOfBirth" type="date" defaultValue={player.dateOfBirth} required /></label><label>النوع<select name="gender" defaultValue={player.gender ?? ""}><option value="">غير محدد</option><option value="1">ذكر</option><option value="2">أنثى</option></select></label><label>الطول بالسنتيمتر<input name="heightCm" type="number" step="0.01" defaultValue={player.heightCm ?? ""} /></label><label>الوزن بالكيلوجرام<input name="weightKg" type="number" step="0.01" defaultValue={player.weightKg ?? ""} /></label><label>القدم المفضلة<select name="preferredFoot" defaultValue={player.preferredFoot ?? ""}><option value="">غير محدد</option><option value="1">اليمنى</option><option value="2">اليسرى</option><option value="3">كلتاهما</option></select></label><label>مركز كرة القدم<input name="footballPosition" defaultValue={player.footballPosition ?? ""} /></label><label>تحديث صورة اللاعب<input name="photo" type="file" accept="image/jpeg,image/png,image/webp" /></label><label>العنوان<input name="address" defaultValue={player.address ?? ""} /></label>{error && <ErrorState message={error} />}<div className="form-actions"><button className="primary-button">حفظ التعديلات</button><button type="button" onClick={() => router.back()}>إلغاء</button></div></form></>;
}
