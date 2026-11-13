"use client";

import { FilePicker } from "../components/file-picker";
import Image from "next/image";
import Link from "next/link";
import { FormEvent, useRef, useState } from "react";
import { Button, EmptyState, ErrorState, LoadingState, PageHeader } from "../components/dashboard-shell";
import { monthToDate } from "../lib/date-range";
import { DashboardRequestError, uploadAsset } from "./dashboard-api";
import { GroupMediaAudienceFields, MediaWithdrawal } from "./group-media-privacy";
import { useGoalData } from "./player-goals";
import styles from "./group-media-privacy.module.css";
import { MediaErasure, MediaStorageNotice } from "./media-lifecycle";

type Target = { id: string; arabicName: string; playerCode?: string; branchName?: string };
type TargetPage = { items: Target[]; hasMore: boolean; selected?: Target | null };
type Asset = { id: string; url: string; contentType: string; caption?: string; playerId?: string; trainingGroupId?: string; trainingSessionId?: string; mediaEventOn?: string; withdrawn: boolean; canWithdraw: boolean; canPublish: boolean; withdrawalNote?: string; retainUntilUtc?: string; erased?: boolean; purged?: boolean; expired?: boolean; canDelete?: boolean };
const galleryPath = "/dashboard/players/media";

export function MediaUpload({ gallery = false }: { gallery?: boolean }) {
  const [review, setReview] = useState<Asset | null>(null);
  const [revision, setRevision] = useState(0);
  const [published, setPublished] = useState(false);
  const showGallery = gallery && !review;
  const publication = useGoalData<TargetPage>(showGallery ? "/api/v1/assets/media-groups?pageSize=1" : null, revision,
    "تعذر التحقق من إمكانية رفع الوسائط.");
  const refresh = () => setRevision(value => value + 1);
  function showSaved() { setReview(null); setPublished(true); refresh(); }
  function showGalleryAgain() { setReview(null); refresh(); }

  return <>
    <PageHeader title={showGallery ? "معرض اللاعبين والمجموعات" : review ? "مراجعة نسخة وسائط جديدة" : "رفع صور وفيديوهات"}
      context="اللاعبون / المعرض" secondaryActions={showGallery ? [{ label: "ذكريات الشهر", href: `${galleryPath}/memories` }] : []}
      action={showGallery && publication.data ? { label: "رفع صور أو فيديو", href: `${galleryPath}/new` } : undefined}
      back={!gallery ? { label: "العودة إلى المعرض", href: galleryPath } : undefined} />
    {published && <p role="status" className="success-message">تم نشر النسخة للجمهور المخوّل.</p>}
    {review && <Button variant="secondary" onClick={showGalleryAgain}>العودة إلى المعرض</Button>}
    <MediaStorageNotice revision={revision} />
    {publication.error && (publication.status === 0 || (publication.status ?? 0) >= 500) &&
      <div className="form-actions"><ErrorState message={publication.error} /><Button variant="secondary" onClick={publication.reload}>إعادة التحقق من الرفع</Button></div>}
    {showGallery ? <StaffMediaGallery revision={revision} onChanged={refresh} onReview={asset => { setPublished(false); setReview(asset); }} /> :
      published ? <div className="form-actions">
        <Link className="primary-button" href={galleryPath}>عرض المعرض</Link>
        <Button variant="secondary" onClick={() => setPublished(false)}>رفع ملف آخر</Button>
      </div> : <MediaPublishForm key={review?.id ?? revision} review={review} onSaved={showSaved} onGallery={gallery ? showGalleryAgain : undefined} />}
  </>;
}

function MediaPublishForm({ review, onSaved, onGallery }: { review: Asset | null; onSaved: () => void; onGallery?: () => void }) {
  const [scope, setScope] = useState<"player" | "group">(review?.trainingGroupId ? "group" : "player");
  const originalTarget = review?.trainingGroupId ?? review?.playerId ?? "";
  const [target, setTarget] = useState(originalTarget);
  const [pinnedTarget, setPinnedTarget] = useState(originalTarget);
  const [search, setSearch] = useState("");
  const [page, setPage] = useState(1);
  const [fileChosen, setFileChosen] = useState(false);
  const [reviewed, setReviewed] = useState(false);
  const [error, setError] = useState("");
  const [uncertain, setUncertain] = useState(false);
  const [busy, setBusy] = useState(false);
  const submitting = useRef(false);
  const query = new URLSearchParams({ page: String(page), pageSize: "25" });
  if (search.trim()) query.set("search", search.trim());
  if (pinnedTarget) query.set("selectedId", pinnedTarget);
  const { data, error: optionsError, status, reload } = useGoalData<TargetPage>(
    `/api/v1/assets/media-${scope === "player" ? "players" : "groups"}?${query}`, 0,
    scope === "player" ? "تعذر تحميل قائمة اللاعبين." : "تعذر تحميل قائمة المجموعات.",
  );
  const selected = data?.selected;
  const targets = selected && !data.items.some(item => item.id === selected.id)
    ? [selected, ...data.items] : data?.items;
  const validTarget = targets?.some(item => item.id === target);
  function invalidateReview() { setReviewed(false); setError(""); }
  function clearTarget() { setTarget(""); setPinnedTarget(""); invalidateReview(); }

  async function upload(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (submitting.current || uncertain || !validTarget || !reviewed) return;
    const form = new FormData(event.currentTarget);
    const file = event.currentTarget.querySelector<HTMLInputElement>('input[name="file"]')?.files?.[0];
    if (!(file instanceof File) || !file.size) { setError("اختر ملفًا يحتوي على صورة أو فيديو."); return; }
    const date = form.get("eventOn");
    const session = form.get("sessionId");
    const subjects = String(form.get("subjectPlayerIds") ?? "");
    const noChildren = form.get("noRecognizableChildren") === "true";
    if (!date || scope === "group" && !noChildren && !subjects) {
      setError("راجع تاريخ التصوير وحدد الأطفال الظاهرين، أو أكد عدم ظهور طفل يمكن التعرف عليه.");
      return;
    }
    submitting.current = true;
    setBusy(true);
    setError("");
    try {
      const saved = await uploadAsset(file, "Media", {
        ...(scope === "player" ? { playerId: target } : { groupId: target }),
        caption: String(form.get("caption") ?? ""), eventOn: String(date),
        ...(session ? { sessionId: String(session) } : {}),
        privacyConfirmed: "true", subjectPlayerIds: subjects, noRecognizableChildren: String(noChildren),
      });
      if (typeof saved?.id !== "string" || !saved.id || typeof saved.url !== "string" || !saved.url) {
        throw new DashboardRequestError("تعذر تأكيد الرفع.", 0);
      }
      onSaved();
    } catch (cause) {
      if (cause instanceof DashboardRequestError && (cause.status === 0 || cause.status >= 500 && cause.code !== "media_inspection_unavailable")) {
        setUncertain(true);
      } else {
        setError(cause instanceof Error ? cause.message : "تعذر رفع الوسائط.");
      }
    } finally {
      submitting.current = false;
      setBusy(false);
    }
  }

  if (uncertain) return <section className="detail-card">
    <ErrorState message="تعذر تأكيد النشر. افتح المعرض للتحقق من وجود النسخة." />
    <div className="form-actions">{onGallery ? <Button onClick={onGallery}>عرض المعرض</Button> : <Link className="primary-button" href={galleryPath}>عرض المعرض</Link>}</div>
  </section>;

  if (status === 401 || status === 403 || status === 404) return <section className="detail-card">
    <EmptyState title="رفع الوسائط غير متاح" message="راجع صلاحية النشر مع مالك الأكاديمية." />
  </section>;

  return <form className="form-shell" onSubmit={event => void upload(event)} aria-busy={busy}>
    {review && <p className="notice">اختر ملفًا جديدًا وراجع جمهور النسخة الجديدة. تظل النسخة السابقة مسحوبة.</p>}
    {error && <ErrorState message={error} />}
    <label>النشر إلى<select value={scope} disabled={busy} onChange={event => { setScope(event.target.value as "player" | "group"); setSearch(""); setPage(1); clearTarget(); }}>
      <option value="player">لاعب محدد · أسرته فقط</option><option value="group">مجموعة · الأسر المخوّلة</option>
    </select></label>
    <label>{scope === "player" ? "بحث باسم اللاعب أو الكود" : "بحث باسم المجموعة"}
      <input type="search" maxLength={100} value={search} disabled={busy}
        onChange={event => { setSearch(event.target.value); setPage(1); clearTarget(); }}
        onKeyDown={event => { if (event.key === "Enter") event.preventDefault(); }} />
    </label>
    {optionsError && <div className="form-actions"><ErrorState message={optionsError} /><Button variant="secondary" onClick={reload}>إعادة المحاولة</Button></div>}
    {!data && !optionsError && <p role="status">جارٍ تحميل القائمة…</p>}
    {data && !targets?.length && <EmptyState message={search.trim() ? "لا توجد نتائج مطابقة في نطاقك." : "لا توجد خيارات متاحة للنشر في نطاقك."} />}
    {!!targets?.length && <label>{scope === "player" ? "اللاعب" : "المجموعة"}<select value={validTarget ? target : ""} disabled={busy} required onChange={event => { setTarget(event.target.value); invalidateReview(); }}>
      <option value="">اختر</option>
      {targets.map(item => <option key={item.id} value={item.id}>{[item.arabicName, item.playerCode ?? item.branchName].filter(Boolean).join(" · ")}</option>)}
    </select></label>}
    {(page > 1 || data?.hasMore) && <div className="form-actions">
      {page > 1 && <Button variant="secondary" disabled={busy} onClick={() => { setPage(page - 1); clearTarget(); }}>الصفحة السابقة</Button>}
      {data?.hasMore && <Button variant="secondary" disabled={busy} onClick={() => { setPage(page + 1); clearTarget(); }}>الصفحة التالية</Button>}
    </div>}
    {validTarget && scope === "group" && <GroupMediaAudienceFields key={target} groupId={target} disabled={busy} onReviewChanged={invalidateReview} />}
    {validTarget && scope === "player" && <label>تاريخ التصوير<input type="date" name="eventOn" required disabled={busy} onChange={invalidateReview} defaultValue={monthToDate().to} /></label>}
    <div hidden={!validTarget}><label>الصورة أو الفيديو<FilePicker aria-label="الصورة أو الفيديو" required name="file" accept="image/jpeg,image/png,image/webp,video/mp4,video/quicktime" disabled={busy || !validTarget}
      onChange={event => { setFileChosen(Boolean(event.currentTarget.files?.length)); invalidateReview(); }} /></label></div>
    {validTarget && <><details className="media-policy-help"><summary>الصيغ وحدود الرفع</summary>
      <p>صورة JPEG أو PNG أو WebP حتى 8 ميجابايت و20 مليون بكسل. فيديو MP4 أو MOV حتى 50 ميجابايت ودقيقتين و1080p و60 إطارًا في الثانية.</p>
      <p>يحفظ التطبيق نسخة معاد ترميزها بدون بيانات موقع الكاميرا.</p>
    </details>
    <label>الوصف · اختياري<input name="caption" maxLength={300} disabled={busy} /></label>
    <div className={styles.confirmPanel}><label className={styles.confirm}>
      <input type="checkbox" name="privacyConfirmed" value="true" required checked={reviewed} disabled={busy || !validTarget || !fileChosen} onChange={event => setReviewed(event.target.checked)} />
      <span>{scope === "player" ? "راجعت الملف. لا يظهر طفل آخر يمكن التعرف عليه." : "راجعت الملف. حددت كل طفل يمكن التعرف عليه في اللقطة."}<small>النشر داخل المعرض فقط. الإعلان يحتاج موافقة منفصلة.</small></span>
    </label></div>
    <div className="form-actions"><button className="primary-button" disabled={busy || !validTarget || !fileChosen || !reviewed}>{busy ? "جارٍ الرفع…" : "رفع ونشر"}</button></div>
    <Link href={`${galleryPath}/memories?${scope === "player" ? "playerId" : "groupId"}=${target}`}>ذكريات الشهر لهذا النطاق</Link>
    </>}
  </form>;
}

function StaffMediaGallery({revision,onReview,onChanged}:{revision:number;onChanged:()=>void;onReview:(asset:Asset)=>void}){
 const {data,error,reload}=useGoalData<Asset[]>("/api/v1/assets/gallery?includeWithdrawn=true",revision,"تعذر تحميل المعرض.");
 return <section><div className="content-record-header"><h2>الصور والفيديوهات</h2><small className="muted">أحدث 100 نسخة · تشمل المسحوبة</small></div>{error&&<><ErrorState message={error}/><Button variant="secondary" onClick={reload}>إعادة المحاولة</Button></>}{!data&&!error&&<LoadingState/>}{data?.length===0&&<EmptyState title="المعرض فارغ" />}<section className="private-gallery">{data?.map(x=><article className={`detail-card ${styles.mediaCard}`} key={x.id}>
 {x.erased||x.expired?<p>هذه النسخة غير متاحة للفتح.</p>:x.contentType.startsWith("video/")?<video src={x.url} controls preload="metadata"/>:<Image unoptimized src={x.url} width={380} height={260} alt={x.caption??"وسائط تدريب"}/>}
 <p>{x.caption}</p><p>{x.trainingSessionId?"مرتبطة بحصة فعلية":"وسائط دون حصة"} · <bdi>{x.mediaEventOn}</bdi></p>
 <p role="status">{x.erased?(x.purged?"تم حذف الملف؛ سجل النشر والحذف محفوظ":"محجوبة عن الجميع؛ إزالة الملف معلقة والمساحة لم تتحرر بعد"):x.expired?"انتهت مدة الاحتفاظ؛ النسخة محجوبة عن الجميع":x.withdrawn?"مسحوبة من الأسر والمدرب · الأصل للمراجعة الإدارية فقط":"نسخة محفوظة · وصول الأسرة مشروط بالتفويض والموافقة"}</p>
 {x.retainUntilUtc&&<p>{x.erased||x.expired?"موعد الاحتفاظ المسجل":"متاحة حتى"} <bdi>{new Date(x.retainUntilUtc).toLocaleString("ar-EG-u-nu-latn")}</bdi></p>}
 {x.withdrawalNote&&<p>ملاحظة إدارية: {x.withdrawalNote}</p>}
 <div className="row-actions">{!x.erased&&!x.expired&&<Link className="button button-secondary" href={x.url} target="_blank">فتح النسخة</Link>}
 {x.canWithdraw&&!x.withdrawn&&!x.erased&&!x.expired&&<MediaWithdrawal id={x.id} onSaved={onChanged}/>}
 {x.canDelete&&!x.erased&&<MediaErasure id={x.id} onSaved={onChanged}/>}
 {x.canPublish&&(x.withdrawn||x.erased||x.expired)&&<button type="button" className="button button-secondary" onClick={()=>onReview(x)}>مراجعة نسخة جديدة</button>}</div>
 </article>)}</section></section>;
}
