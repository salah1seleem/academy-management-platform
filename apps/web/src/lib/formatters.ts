const arabicDate = new Intl.DateTimeFormat("ar-EG", {
  year: "numeric",
  month: "short",
  day: "numeric",
  timeZone: "UTC",
});

const arabicDateTime = new Intl.DateTimeFormat("ar-EG", {
  year: "numeric",
  month: "short",
  day: "numeric",
  hour: "numeric",
  minute: "2-digit",
});

export const planTypeLabels: Record<string, string> = {
  Duration: "بالمدة",
  Sessions: "بالحصص",
  Combined: "بالمدة والحصص",
};

export const workflowStatusLabels: Record<string, string> = {
  Active: "فعال",
  Inactive: "متوقف",
  Scheduled: "مجدول",
  Expiring: "ينتهي قريبًا",
  Expired: "منتهي",
  Frozen: "مجمد",
  Cancelled: "ملغي",
  Created: "تم الإنشاء",
  Pending: "قيد الانتظار",
  PendingPayment: "في انتظار الدفع",
  PaymentInProgress: "جارٍ الدفع",
  Confirmed: "مؤكد",
  Paid: "مدفوع",
  Failed: "فشل",
  Draft: "مسودة",
  Published: "منشور",
  UnderReview: "قيد المراجعة",
  Approved: "مقبول",
  Rejected: "مرفوض",
  Present: "حاضر",
  Absent: "غائب",
  NotRecorded: "غير مسجل",
};

export const discountTypeLabels: Record<string, string> = {
  Percentage: "نسبة مئوية",
  FixedAmount: "مبلغ ثابت",
};

export function formatMoneyAr(amount: unknown, currency: unknown = "EGP") {
  const value = typeof amount === "number" ? amount : Number(amount ?? 0);
  const code = typeof currency === "string" && currency ? currency : "EGP";
  if (code === "EGP") {
    return `${new Intl.NumberFormat("ar-EG", { maximumFractionDigits: 2 }).format(value)} جنيه`;
  }
  try {
    return new Intl.NumberFormat("ar-EG", { style: "currency", currency: code, currencyDisplay: "name", maximumFractionDigits: 2 }).format(value);
  } catch {
    return `${new Intl.NumberFormat("ar-EG", { maximumFractionDigits: 2 }).format(value)} ${code}`;
  }
}

export function formatDateAr(value?: string | null) {
  if (!value) return "—";
  const date = new Date(value.length === 10 ? `${value}T00:00:00Z` : value);
  return Number.isNaN(date.valueOf()) ? value : arabicDate.format(date);
}

export function formatDateTimeAr(value?: string | null) {
  if (!value) return "—";
  const date = new Date(value);
  return Number.isNaN(date.valueOf()) ? value : arabicDateTime.format(date);
}

export function statusLabel(value?: string | null) {
  return value ? workflowStatusLabels[value] ?? "حالة غير معروفة" : "غير محدد";
}

export function planTypeLabel(value?: string | null) {
  return value ? planTypeLabels[value] ?? "نوع غير معروف" : "غير محدد";
}
