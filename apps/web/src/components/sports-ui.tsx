"use client";

import Link from "next/link";
import Image from "next/image";
import { Activity, ArrowUpLeft, CalendarDays, Check, CircleHelp, ClipboardList, ReceiptText, RefreshCcw, Trophy, UserPlus, X } from "lucide-react";
import { CSSProperties, ReactNode } from "react";
import { formatDateAr } from "../lib/formatters";

export function SportsHero({ name, academy, date, children }: { name: string; academy: string; date?: string; children?: ReactNode }) {
  return <section className="sports-hero" aria-label="ملخص الأكاديمية">
    <div className="sports-hero-copy"><span className="sports-eyebrow"><Trophy aria-hidden="true" /> كل يوم تدريب.. خطوة للأمام</span>
      <h2>أهلًا، {name || "بك"}</h2><p>إليك ملخص أداء {academy || "الأكاديمية"} اليوم</p>
      {date && <span className="hero-date"><CalendarDays aria-hidden="true" />مرجع التقرير: {formatDateAr(date)}</span>}
      {children}
    </div><div className="pitch-pattern" aria-hidden="true"><span /></div>
  </section>;
}

export function IdentityMark({ name, detail, photo, large = false }: { name: string; detail?: string; photo?: string; large?: boolean }) {
  const initials = name.trim().split(/\s+/).slice(0, 2).map(part => part[0]).join(" ");
  // Demo assets only; never send a child's identity to an arbitrary image host.
  const safePhoto = photo && /^\/demo-assets\/[a-zA-Z0-9/_-]+\.(svg|png|jpe?g|webp)$/.test(photo) ? photo : null;
  return <span className={`sports-identity ${large ? "sports-identity-large" : ""}`}>{safePhoto ? <Image className="identity-monogram" src={safePhoto} width={large ? 80 : 42} height={large ? 80 : 42} alt={`صورة ${name}`} /> : <span className="identity-monogram" aria-hidden="true">{initials}</span>}<span><strong>{name}</strong>{detail && <small>{detail}</small>}</span></span>;
}

export function SportsQuickActions({ coach = false }: { coach?: boolean }) {
  const actions = coach ? [
    { label: "تسجيل حضور", detail: "جلسات مجموعاتك", href: "/dashboard/attendance/sessions", icon: Activity },
    { label: "إنشاء تقييم", detail: "تابع تطور اللاعبين", href: "/dashboard/evaluations/new", icon: ClipboardList },
  ] : [
    { label: "تسجيل لاعب", detail: "انضمام جديد للأكاديمية", href: "/dashboard/players/new", icon: UserPlus },
    { label: "إضافة مدرب", detail: "حساب فردي وإسناد مجموعات", href: "/dashboard/academy/coaches/create", icon: UserPlus },
    { label: "تجديد اشتراك", detail: "استمرار رحلة التدريب", href: "/dashboard/subscriptions/renew/new", icon: RefreshCcw },
    { label: "تسجيل حضور", detail: "متابعة حصص اليوم", href: "/dashboard/attendance/sessions", icon: Activity },
    { label: "إنشاء تقييم", detail: "قياس الأداء والتطور", href: "/dashboard/evaluations/new", icon: ClipboardList },
    { label: "التقرير المالي", detail: "التحصيلات والإيصالات", href: "/dashboard/reports/financial", icon: ReceiptText },
  ];
  return <nav className="sports-quick-actions" aria-label="الإجراءات التشغيلية السريعة">{actions.map(({ label, detail, href, icon: Icon }) => <Link key={href} href={href}><span className="quick-action-icon"><Icon aria-hidden="true" /></span><span><b>{label}</b><small>{detail}</small></span><ArrowUpLeft className="quick-action-arrow" aria-hidden="true" /></Link>)}</nav>;
}

export function ProgressTrack({ value, total, label }: { value: number; total: number; label: string }) {
  const percent = total > 0 ? Math.min(100, Math.max(0, value / total * 100)) : 0;
  return <div className="sports-progress" role="progressbar" aria-label={label} aria-valuemin={0} aria-valuemax={Math.max(0, total)} aria-valuenow={value} aria-valuetext={`${value} من ${total}`}><span style={{ "--progress": `${percent}%` } as CSSProperties} /></div>;
}

export function AttendanceChoice({ value, selected, onClick }: { value: string; selected: boolean; onClick: () => void }) {
  const Icon = value === "Present" ? Check : value === "Absent" ? X : CircleHelp;
  return <button type="button" data-status={value} className={selected ? "selected" : ""} aria-pressed={selected} onClick={onClick}><Icon aria-hidden="true" />{value === "Present" ? "حاضر" : value === "Absent" ? "غائب" : "لم يُسجل"}</button>;
}
