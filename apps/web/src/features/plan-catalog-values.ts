export const planCatalogPath = "/dashboard/subscriptions/plans";

export const planServiceLabels: Record<string, string> = {
  Training: "تدريب", DayPass: "زيارة يومية", OpenSwim: "سباحة حرة",
  FacilityBooking: "حجز مرفق لشريك", Event: "بطولة أو معسكر",
};

export const planTrainingFormats: Record<string, string> = {
  Group: "جماعي", Private: "خاص", SemiPrivate: "شبه خاص", Season: "موسم",
};

export function planCatalogHref(path: string, search: string) {
  return search.trim() ? `${path}?${new URLSearchParams({ q: search.trim() })}` : path;
}

const number = new Intl.NumberFormat("ar-EG");

export function planLimits({ durationDays, durationMonths, sessionCount }: {
  durationDays?: number | null; durationMonths?: number | null; sessionCount?: number | null;
}) {
  const limits: string[] = [];
  if (durationMonths) limits.push(durationMonths === 1 ? "شهر تقويمي" : durationMonths === 2 ? "شهران تقويميان" : `${number.format(durationMonths)} ${durationMonths <= 10 ? "أشهر تقويمية" : "شهرًا تقويميًا"}`);
  else if (durationDays) limits.push(durationDays === 1 ? "يوم واحد" : durationDays === 2 ? "يومان" : `${number.format(durationDays)} ${durationDays <= 10 ? "أيام" : "يومًا"}`);
  if (sessionCount) limits.push(sessionCount === 1 ? "حصة واحدة" : sessionCount === 2 ? "حصتان" : `${number.format(sessionCount)} ${sessionCount <= 10 ? "حصص" : "حصة"}`);
  return limits.join(" · ");
}
