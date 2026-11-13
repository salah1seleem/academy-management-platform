const arabicDate = new Intl.DateTimeFormat("ar-EG-u-nu-latn", {
  year: "numeric",
  month: "short",
  day: "numeric",
  timeZone: "UTC",
});

const dateTimeOptions = {
  year: "numeric",
  month: "short",
  day: "numeric",
  hour: "numeric",
  minute: "2-digit",
} as const;
const arabicDateTime = new Intl.DateTimeFormat("ar-EG-u-nu-latn", dateTimeOptions);
const receiptDateTime = new Intl.DateTimeFormat("ar-EG-u-nu-latn", { ...dateTimeOptions, timeZone: "UTC" });
const offsetTimestamp = /^(\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?)(?:Z|[+-]\d{2}:\d{2})$/;

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
  Published: "منشور", PublishedInternal: "للاستخدام الداخلي",
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

export function formatMoneyAr(amount: unknown, currency: unknown = "EGP", showFraction = false) {
  const value = typeof amount === "number" ? amount : Number(amount ?? 0);
  const code = typeof currency === "string" && currency ? currency : "EGP";
  const precision = { maximumFractionDigits: 2, ...(showFraction && !Number.isInteger(value) ? { minimumFractionDigits: 2 } : {}) };
  if (code === "EGP") {
    return `${new Intl.NumberFormat("ar-EG-u-nu-latn", precision).format(value)} جنيه`;
  }
  try {
    return new Intl.NumberFormat("ar-EG-u-nu-latn", { style: "currency", currency: code, currencyDisplay: "name", ...precision }).format(value);
  } catch {
    return `${new Intl.NumberFormat("ar-EG-u-nu-latn", precision).format(value)} ${code}`;
  }
}

export function formatDateAr(value?: string | null) {
  if (!value) return "—";
  const date = new Date(value.length === 10 ? `${value}T00:00:00Z` : value);
  return Number.isNaN(date.valueOf()) ? value : arabicDate.format(date);
}

export function formatSessionDateAr(value: string) {
  const date = new Date(`${value.slice(0, 10)}T12:00:00Z`);
  return Number.isNaN(date.valueOf()) ? value : `${new Intl.DateTimeFormat("ar-EG-u-nu-latn", { weekday: "long", timeZone: "UTC" }).format(date)} · ${formatDateAr(value)}`;
}

export function formatTimeAr(value: string) {
  const match = /^(\d{2}):(\d{2})/.exec(value);
  if (!match) return "—";
  const hour = Number(match[1]);
  return `${hour % 12 || 12}:${match[2]} ${hour >= 12 ? "مساءً" : "صباحًا"}`;
}

export function formatStaffCheckout(time?: string | null, date?: string | null, workDate?: string) {
  if (!time) return "—";
  return `${formatTimeAr(time)}${date && date !== workDate ? ` · ${formatDateAr(date)}` : ""}`;
}

export function formatDateTimeAr(value?: string | null) {
  if (!value) return "—";
  const date = new Date(value);
  return Number.isNaN(date.valueOf()) ? value : arabicDateTime.format(date);
}

function formatReceiptClock(date: Date) {
  return receiptDateTime.format(date).replace(/\s(?=[صم]$)/u, "\u00a0");
}

export function formatReceiptTimestampAr(utcValue: unknown, academyValue: unknown) {
  if (typeof utcValue !== "string" || !offsetTimestamp.test(utcValue)) return null;
  const utc = new Date(utcValue);
  if (!Number.isFinite(utc.valueOf())) return null;
  if (typeof academyValue === "string") {
    const wallTime = offsetTimestamp.exec(academyValue)?.[1];
    if (wallTime && Date.parse(academyValue) === utc.valueOf()) {
      // Format the clock fields supplied with the academy offset without a second conversion.
      return { text: formatReceiptClock(new Date(`${wallTime}Z`)), zone: "بتوقيت الأكاديمية" };
    }
  }
  return { text: formatReceiptClock(utc), zone: "بالتوقيت العالمي" };
}

export function statusLabel(value?: string | null) {
  return value ? workflowStatusLabels[value] ?? "حالة غير معروفة" : "غير محدد";
}

export function planTypeLabel(value?: string | null) {
  return value ? planTypeLabels[value] ?? "نوع غير معروف" : "غير محدد";
}
