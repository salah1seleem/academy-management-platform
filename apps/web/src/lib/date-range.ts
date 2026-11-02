export function monthToDate(now = new Date()): { from: string; to: string } {
  const to = new Intl.DateTimeFormat("en-CA", { timeZone: "Africa/Cairo", year: "numeric", month: "2-digit", day: "2-digit" }).format(now);
  return { from: `${to.slice(0, 8)}01`, to };
}
