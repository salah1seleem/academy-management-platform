// Football Demo has an explicit Africa/Cairo timezone. Epoch-seconds modulo
// an hour does NOT describe its current wall clock, especially after midnight.
export function endedFootballSlot(now: Date) {
  const parts = Object.fromEntries(new Intl.DateTimeFormat("en-CA", {
    timeZone: "Africa/Cairo", year: "numeric", month: "2-digit", day: "2-digit",
    hour: "2-digit", minute: "2-digit", second: "2-digit", hourCycle: "h23",
  }).formatToParts(now).map(p => [p.type, p.value]));
  const elapsed = Number(parts.hour) * 3600 + Number(parts.minute) * 60 + Number(parts.second);
  if (elapsed < 3) return null; // No completed same-day slot yet; never invent one.
  const end = Math.max(1, elapsed - 5);
  const time = (s: number) => `${String(Math.floor(s / 3600)).padStart(2, "0")}:${String(Math.floor(s / 60) % 60).padStart(2, "0")}:${String(s % 60).padStart(2, "0")}`;
  return { sessionDate: `${parts.year}-${parts.month}-${parts.day}`, startTime: time(end - 1), endTime: time(end) };
}

export async function endedFootballSession() {
  // At most the first three seconds of the Cairo day; no clock/rule override.
  for (let attempt = 0; attempt < 5; attempt++) {
    const slot = endedFootballSlot(new Date());
    if (slot) return slot;
    await new Promise(resolve => setTimeout(resolve, 1000));
  }
  throw new Error("Could not establish an actually ended Football Demo session.");
}
