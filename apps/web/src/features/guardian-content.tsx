"use client";

import Image from "next/image";
import Link from "next/link";
import { useEffect, useId, useState } from "react";
import { EmptyState, ErrorState, LoadingState } from "../components/dashboard-shell";
import { GuardianShell } from "../components/guardian-shell";
import { formatDateAr, formatMoneyAr } from "../lib/formatters";
import {GroupMediaConsent} from "./group-media-privacy";
import {useGoalData} from "./player-goals";
import {MediaStorageNotice}from"./media-lifecycle";

type CatalogSection = { sportId: string; sport: string; items: { id: string; arabicName: string; arabicDescription?: string; imageReference: string; displayPrice?: number; currency?: string; discountPercentage?: number }[] };
const categoryLabels = { Breakfast: "الإفطار", Lunch: "الغداء", Dinner: "العشاء" } as const;
type NutritionCategory = keyof typeof categoryLabels;

function nutritionCategory(value?: string): NutritionCategory {
  return value === "Lunch" || value === "Dinner" ? value : "Breakfast";
}

function nutritionQuery(category: NutritionCategory, day?: string) {
  const query = new URLSearchParams({ category });
  if (day && /^\d{4}-\d{2}-\d{2}$/.test(day)) query.set("day", day);
  return query;
}

function mealHref(id: string, playerId: string, category: NutritionCategory, day?: string) {
  const query = nutritionQuery(category, day);
  query.set("player", playerId);
  return `/guardian/nutrition/${encodeURIComponent(id)}?${query}`;
}
const typeLabels: Record<string, string> = { Injury: "إصابة", Consultation: "استشارة", Image: "صورة", Video: "فيديو" };
const medicalStatus: Record<string, string> = { Open: "مفتوح", Monitoring: "متابعة", Resolved: "مغلق" };

export function GuardianSportCatalog() {
  const [sections, setSections] = useState<CatalogSection[] | null>(null); const [error, setError] = useState("");
  useEffect(() => { fetch("/api/v1/guardian/catalog").then(r => { if (!r.ok) throw new Error(); return r.json(); }).then(value => setSections(Array.isArray(value) ? value : [])).catch(() => setError("تعذر تحميل المنتجات الرياضية.")); }, []);
  return <section className="guardian-section catalog-section" aria-labelledby="catalog-heading"><div className="guardian-section-title"><h2 id="catalog-heading">المنتجات الرياضية</h2><span>كتالوج للعرض فقط</span></div>{error && <ErrorState message={error} />}{!sections && !error ? <LoadingState /> : sections?.length === 0 ? <EmptyState message="لا توجد منتجات منشورة لرياضات الأبناء." /> : sections?.map(section => <div className="catalog-sport" key={section.sportId}><h3>{section.sport}</h3><div className="catalog-grid">{section.items.map(item => <article key={item.id}><Image src={item.imageReference} alt="" width={640} height={420} /><div><h4>{item.arabicName}</h4>{item.arabicDescription && <p>{item.arabicDescription}</p>}{item.displayPrice != null && <span className="display-price">{formatMoneyAr(item.displayPrice, item.currency)}</span>}{item.discountPercentage != null && item.discountPercentage > 0 && <small>خصم عرضي {item.discountPercentage}%</small>}</div></article>)}</div></div>)}</section>;
}

type Meal = { id: string; arabicName: string };
type DailyNutrition = { breakfast?: Meal; lunch?: Meal; dinner?: Meal; disclaimer?: string; isTrainingDay?: boolean };

export function DailyNutritionSummary({ playerId, day, full = false, showTitle = true }: { playerId: string; day?: string; full?: boolean; showTitle?: boolean }) {
  const [revision, setRevision] = useState(0);
  const query = day ? `?date=${encodeURIComponent(day)}` : "";
  const { data, error } = useGoalData<DailyNutrition>(
    `/api/v1/guardian/children/${playerId}/nutrition/recommendations${query}`, revision, "تعذر تحميل اقتراحات اليوم.",
  );
  return <section className="profile-section">
    {showTitle && <h2>اقتراحات اليوم</h2>}
    {error ? <><ErrorState message={error}/><button type="button" className="secondary-button" onClick={() => setRevision(value => value + 1)}>إعادة المحاولة</button></> : !data ? <LoadingState /> : <>
      {data.breakfast || data.lunch || data.dinner ? <div className="row-actions">
        {([["breakfast", "Breakfast"], ["lunch", "Lunch"], ["dinner", "Dinner"]] as const).map(([key, category]) => {
          const meal = data[key];
          return meal && <Link className="secondary-button" key={key} href={mealHref(meal.id, playerId, category, day)}>{categoryLabels[category]} · {meal.arabicName}</Link>;
        })}
      </div> : <EmptyState message="لا توجد اقتراحات غذائية لهذا اليوم." />}
      {full && <p>{data.disclaimer}</p>}
    </>}
  </section>;
}

type NutritionCard = { id: string; arabicName: string; arabicDescription: string; imageReference: string; category: keyof typeof categoryLabels; dataStatus: string };
export function NutritionLibrary({ playerId, day, initialCategory }: { playerId: string; day?: string; initialCategory?: string }) {
  const [category, setCategory] = useState(() => nutritionCategory(initialCategory));
  const [revision, setRevision] = useState(0);
  const prefix = useId();
  const categories = Object.entries(categoryLabels) as [keyof typeof categoryLabels, string][];
  const { data: items, error } = useGoalData<NutritionCard[]>(`/api/v1/guardian/nutrition?category=${category}`, revision, "تعذر تحميل هذا القسم من المكتبة.");
  return <GuardianShell>
    <Link className="back-link" href={`/guardian/children/${playerId}`}>العودة لملف الطفل</Link>
    <header className="guardian-page-header"><h1>مكتبة التغذية</h1><p>معلومات عامة للتغذية. لا تُستخدم كخطة علاجية.</p></header>
    <DailyNutritionSummary playerId={playerId} day={day} full />
    <div className="nutrition-tabs" role="tablist" aria-label="أقسام التغذية">
      {categories.map(([value, label], index) => <button type="button" role="tab" id={`${prefix}-${value}`}
        aria-selected={category === value} aria-controls={`${prefix}-panel`} tabIndex={category === value ? 0 : -1}
        className={category === value ? "active" : ""} key={value} onClick={() => setCategory(value)}
        onKeyDown={event => {
          const last = categories.length - 1;
          const next = event.key === "ArrowLeft" ? (index + 1) % categories.length : event.key === "ArrowRight" ? (index + last) % categories.length : event.key === "Home" ? 0 : event.key === "End" ? last : null;
          if (next === null) return;
          event.preventDefault();
          const [target] = categories[next];
          setCategory(target);
          document.getElementById(`${prefix}-${target}`)?.focus();
        }}>{label}</button>)}
    </div>
    <div role="tabpanel" id={`${prefix}-panel`} aria-labelledby={`${prefix}-${category}`} tabIndex={0}>
    {error ? <><ErrorState message={error} /><button className="secondary-button" type="button" onClick={() => setRevision(value => value + 1)}>إعادة تحميل القسم</button></>
      : !items ? <LoadingState /> : items.length === 0 ? <EmptyState message="لا توجد مواد منشورة في هذا القسم." />
      : <div className="nutrition-grid">{items.map(item => <Link href={mealHref(item.id, playerId, category, day)} key={item.id}><Image src={item.imageReference} alt="" width={640} height={420} /><h2>{item.arabicName}</h2><p>{item.arabicDescription}</p>{item.dataStatus !== "Reviewed" && <small>{item.dataStatus === "DemoUnreviewed" ? "بيانات تجريبية غير مراجعة" : "حالة المراجعة غير متاحة"}</small>}</Link>)}</div>}
    </div>
  </GuardianShell>;
}

type NutritionDetailsData = { id: string; arabicName: string; arabicDescription: string; imageReference: string; servingDescription: string; calories?: number; proteinGrams?: number; carbohydratesGrams?: number; fatGrams?: number; dataStatus: string; sourceDescription?: string };
export function NutritionDetails({ id, playerId, category, day }: { id: string; playerId?: string; category?: string; day?: string }) {
  const { data: item, error, reload } = useGoalData<NutritionDetailsData>(
    `/api/v1/guardian/nutrition/${encodeURIComponent(id)}`, 0, "تعذر تحميل تفاصيل الوجبة.",
  );
  const back = playerId ? `/guardian/children/${encodeURIComponent(playerId)}/nutrition?${nutritionQuery(nutritionCategory(category), day)}` : "/guardian";
  return <GuardianShell>
    <Link className="back-link" href={back}>{playerId ? "العودة للمكتبة" : "العودة إلى الرئيسية"}</Link>
    {error ? <><ErrorState message={error} /><button type="button" className="secondary-button" onClick={reload}>إعادة المحاولة</button></>
      : !item ? <LoadingState /> : <article className="meal-details">
        <Image src={item.imageReference} alt="" width={640} height={420} priority />
        <h1>{item.arabicName}</h1><p>{item.arabicDescription}</p><b>الحصة: {item.servingDescription}</b>
        <div className="nutrition-facts">
          <Fact label="السعرات الحرارية" value={item.calories} unit="سعر" />
          <Fact label="البروتين" value={item.proteinGrams} unit="جرام" />
          <Fact label="الكربوهيدرات" value={item.carbohydratesGrams} unit="جرام" />
          <Fact label="الدهون" value={item.fatGrams} unit="جرام" />
        </div>
        <div className={`review-status ${item.dataStatus.toLowerCase()}`}>{item.dataStatus === "DemoUnreviewed" ? "بيانات تجريبية غير مراجعة" : "بيانات مراجعة"}{item.sourceDescription && <small>{item.sourceDescription}</small>}</div>
      </article>}
  </GuardianShell>;
}
function Fact({ label, value, unit }: { label: string; value?: number; unit: string }) { return <div><span>{label}</span><b>{value ?? "غير متاح"}</b>{value != null && <small>{unit}</small>}</div>; }

type Medical = { id: string; type: string; arabicTitle: string; recordDate: string; arabicDescription: string; status: string; guardianVisibleNotes?: string };
export function GuardianMedical({ playerId }: { playerId: string }) { const [items, setItems] = useState<Medical[] | null>(null); const [error, setError] = useState(""); useEffect(() => { fetch(`/api/v1/guardian/children/${playerId}/medical`).then(r => { if (!r.ok) throw new Error(); return r.json(); }).then(setItems).catch(() => setError("تعذر تحميل السجلات المنشورة.")); }, [playerId]); return <GuardianShell><Link className="back-link" href={`/guardian/children/${playerId}`}>العودة لملف الطفل</Link><header className="guardian-page-header"><span>سجلات منشورة فقط</span><h1>الإصابات والاستشارات الطبية</h1></header>{error && <ErrorState message={error} />}{!items && !error ? <LoadingState /> : items?.length === 0 ? <EmptyState message="لا توجد إصابات أو استشارات منشورة" /> : <div className="medical-list">{items?.map(item => <article key={item.id}><div><span>{typeLabels[item.type]}</span><b>{medicalStatus[item.status]}</b></div><h2>{item.arabicTitle}</h2><small>{formatDateAr(item.recordDate)}</small><p>{item.arabicDescription}</p>{item.guardianVisibleNotes && <p className="guardian-note">{item.guardianVisibleNotes}</p>}</article>)}</div>}</GuardianShell>; }

type Media = { id: string; type: string; mediaReference: string; thumbnailReference?: string; arabicCaption?: string; capturedAtUtc?: string };
export function GuardianGallery({playerId}:{playerId:string}) {
  const [revision,setRevision] = useState(0);
  const reload = () => setRevision(value => value + 1);
  const {data:items,error} = useGoalData<Media[]>(`/api/v1/guardian/children/${playerId}/media`,revision,"تعذر تحميل معرض الطفل.");
  const {data:privateItems,error:privateError} = useGoalData<{id:string;url:string;contentType:string;caption:string|null}[]>(`/api/v1/assets/gallery?playerId=${playerId}`,revision,"تعذر تحميل الوسائط المنشورة.");
  const failed = error || privateError;
  return <GuardianShell>
    <Link className="back-link" href={`/guardian/children/${playerId}`}>العودة إلى ملف الطفل</Link>
    <header className="guardian-page-header"><h1>الصور والفيديوهات</h1><Link className="guardian-card-cta" href={`/guardian/children/${playerId}/memories`}>ذكريات الشهر</Link></header>
    {failed && <><ErrorState message={failed} /><button className="guardian-card-cta" type="button" onClick={reload}>إعادة المحاولة</button></>}
    {(!items && !error || !privateItems && !privateError) && <LoadingState />}
    <div className="gallery-grid">
      {privateItems?.map(item => <figure key={item.id}>{item.contentType.startsWith("video/") ? <video src={item.url} controls preload="none" /> :
        <Image unoptimized src={item.url} alt={item.caption || "صورة من التمرين"} width={640} height={420} />} {item.caption && <figcaption>{item.caption}</figcaption>}</figure>)}
      {items?.map(item => <figure key={item.id}><Image src={item.thumbnailReference ?? item.mediaReference} alt={item.arabicCaption ?? "وسائط اللاعب"} width={640} height={420} />
        <figcaption><span>{typeLabels[item.type]}</span>{item.arabicCaption}</figcaption></figure>)}
    </div>
    {items?.length === 0 && privateItems?.length === 0 && <EmptyState message="لا توجد صور أو فيديوهات منشورة لهذا الطفل." />}
    <GroupMediaConsent playerId={playerId} onSaved={reload} />
    <MediaStorageNotice />
  </GuardianShell>;
}
