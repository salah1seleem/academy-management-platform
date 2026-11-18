import { expect, type APIRequestContext, type Page, type Locator } from "@playwright/test";

export const placementId = (n: number) => `5a000000-0000-0000-0000-${String(n).padStart(12, "0")}`;
type SessionApi = Pick<APIRequestContext, "get" | "post">;
type Session = { id: string; trainingGroupId: string; sessionDate: string; startTime: string; endTime: string; status: string };
type Enrollment = {
  id: string; trainingGroupId: string | null; registrationFormat: string;
  subscription: { id: string; remainingSessions?: number | null; endDate?: string | null } | null;
  transfers?: { effectiveDate: string; groupName: string }[];
  sessionMoves?: unknown[];
  upcoming?: { id: string; startTime: string }[];
};
export type PlacementPlayer = { playerCode: string; enrollments: Enrollment[] };

export async function placementRead<T>(api: Pick<APIRequestContext, "get">, path: string): Promise<T> {
  const response = await api.get(path);
  expect(response.status(), `${path}: ${await response.text()}`).toBe(200);
  return response.json() as Promise<T>;
}

export async function placementWrite(api: APIRequestContext, path: string, data: unknown, key: string = crypto.randomUUID()) {
  const csrf = await placementRead<{ token: string }>(api, "/api/v1/auth/csrf");
  return api.post(path, { headers: { "X-CSRF-TOKEN": csrf.token, "Idempotency-Key": key }, data });
}

export function placementEnrollment(player: PlacementPlayer, id: string) {
  const found = player.enrollments.find(row => row.id === id);
  if (!found) throw new Error(`Synthetic enrollment ${id} is missing`);
  return found;
}

export function serviceIdentity(player: PlacementPlayer) {
  return { playerCode: player.playerCode, services: player.enrollments.map(row => ({
    id: row.id, format: row.registrationFormat, periodId: row.subscription?.id ?? null,
    remaining: row.subscription?.remainingSessions ?? null, endDate: row.subscription?.endDate ?? null,
  })).sort((a, b) => a.id.localeCompare(b.id)) };
}

export function afterDays(day: string, days: number) {
  expect(day).toMatch(/^\d{4}-\d{2}-\d{2}$/);
  const date = new Date(`${day}T12:00:00Z`);
  date.setUTCDate(date.getUTCDate() + days);
  return date.toISOString().slice(0, 10);
}

// Reuse only the exact dated session. Never move a historical fixture, change
// the application clock, or silently substitute a cancelled/different slot.
export async function ensurePlacementSession(api: SessionApi, group: string, date: string, start: string, end: string) {
  const path = `/api/v1/attendance/sessions?${new URLSearchParams({ groupId: group, from: date, to: date })}`;
  const find = async () => {
    const rows = await placementRead<Session[]>(api, path);
    const matches = rows.filter(row => row.trainingGroupId === group && row.sessionDate === date && row.startTime === `${start}:00`);
    expect(matches.length).toBeLessThanOrEqual(1);
    if (!matches.length) return null;
    expect(matches[0]).toMatchObject({ endTime: `${end}:00`, status: "Scheduled" });
    return matches[0];
  };
  const prior = await find();
  if (prior) return prior;
  const csrf = await placementRead<{ token: string }>(api, "/api/v1/auth/csrf");
  let response;
  try {
    response = await api.post("/api/v1/attendance/sessions", {
      headers: { "X-CSRF-TOKEN": csrf.token },
      data: { trainingGroupId: group, sessionDate: date, startTime: `${start}:00`, endTime: `${end}:00`, notes: "حصة قبول اصطناعية للتسكين — لا بيانات عملاء" },
    });
  } catch (cause) {
    const saved = await find();
    if (saved) return saved;
    throw cause;
  }
  if (response.status() === 409) {
    const concurrent = await find();
    if (concurrent) return concurrent;
  }
  expect(response.status(), await response.text()).toBe(201);
  const saved = await find();
  expect(saved?.id).toBe((await response.json()).id);
  return saved!;
}

export async function preparePlacementSessions(api: APIRequestContext) {
  const calendar = await placementRead<{ today: string }>(api, "/api/v1/academy/calendar");
  const date = afterDays(calendar.today, 7);
  const group = await placementRead<{ group: { registrationFormat: string } }>(api, `/api/v1/manage/groups/${placementId(1)}/details`);
  expect(group.group.registrationFormat).toBe("Group");
  const source = await ensurePlacementSession(api, placementId(1), date, "15:00", "16:00");
  const target = await ensurePlacementSession(api, placementId(50), date, "17:00", "18:00");
  return { today: calendar.today, date, source, target };
}

export async function ensurePlacement(api: APIRequestContext, player: number, enrollment: number) {
  const row = await placementRead<PlacementPlayer>(api, `/api/v1/people/players/${placementId(player)}`);
  const current = placementEnrollment(row, placementId(enrollment));
  expect(current.subscription?.id).toBeTruthy();
  if (current.trainingGroupId) {
    expect(current.trainingGroupId).toBe(placementId(1));
    return;
  }
  const root = `/api/v1/manage/groups/${placementId(1)}/placement`;
  const preview = await placementWrite(api, `${root}/preview`, { enrollmentIds: [current.id] });
  expect(preview.status(), await preview.text()).toBe(200);
  const response = await placementWrite(api, root, { enrollmentIds: [current.id], fingerprint: (await preview.json()).fingerprint });
  expect(response.ok(), await response.text()).toBe(true);
}

export async function assertPlacementViewport(page: Page, width: number) {
  const measured = await page.evaluate(() => ({
    viewport: innerWidth,
    document: document.documentElement.scrollWidth,
  }));
  // A mobile browser can expand its layout viewport to accommodate overflow.
  // Comparing scrollWidth only with innerWidth would accept that expanded page.
  expect(measured.viewport, JSON.stringify(measured)).toBe(width);
  expect(measured.document, JSON.stringify(measured)).toBeLessThanOrEqual(width + 1);
}

export async function setPlacementAppearance(page: Page, mode: "light" | "dark") {
  const shell = page.locator('[data-workspace-design="v5"]');
  if (await shell.getAttribute("data-mode") !== mode) {
    await page.getByRole("button", { name: mode === "light" ? "تفعيل المظهر الفاتح" : "تفعيل المظهر الداكن", exact: true }).click();
  }
  await expect(shell).toHaveAttribute("data-mode", mode);
}

export async function assertPlacementContrast(region: Locator, selector = ".placement-choice strong, .placement-choice small, .placement-candidates a, .placement-candidates li, .placement-preview h4, .placement-preview p, .placement-preview strong, .placement-preview span") {
  const samples = await region.locator(selector).evaluateAll(nodes => {
    type RGB = [number, number, number];
    const rgba = (value: string) => {
      const match = /^rgba?\((.+)\)$/.exec(value);
      if (!match) throw new Error(`Unsupported computed color: ${value}`);
      const values = match[1].split(/[,\s/]+/).map(Number);
      return { rgb: values.slice(0, 3) as RGB, alpha: values[3] ?? 1 };
    };
    const over = (rgb: RGB, alpha: number, backdrop: RGB) => rgb.map((channel, i) => channel * alpha + backdrop[i] * (1 - alpha)) as RGB;
    const luminance = (rgb: RGB) => rgb.map(channel => {
      const c = channel / 255;
      return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
    }).reduce((sum, channel, i) => sum + channel * [0.2126, 0.7152, 0.0722][i], 0);
    return nodes.filter(node => node.getClientRects().length && node.textContent?.trim()).map(node => {
      const ancestry: Element[] = [];
      for (let current: Element | null = node; current; current = current.parentElement) ancestry.unshift(current);
      let background: RGB = [255, 255, 255];
      let uncoveredImage = false;
      for (const ancestor of ancestry) {
        const style = getComputedStyle(ancestor);
        if (Number(style.opacity) !== 1) throw new Error("Contrast sample has translucent ancestry");
        const color = rgba(style.backgroundColor);
        if (color.alpha === 1) uncoveredImage = false;
        if (style.backgroundImage !== "none") uncoveredImage = true;
        background = over(color.rgb, color.alpha, background);
      }
      if (uncoveredImage) throw new Error("Contrast sample has an uncovered background image; visual review required");
      const foreground = rgba(getComputedStyle(node).color);
      const ink = luminance(over(foreground.rgb, foreground.alpha, background)), paper = luminance(background);
      return { text: node.textContent?.trim().slice(0, 100), ratio: (Math.max(ink, paper) + 0.05) / (Math.min(ink, paper) + 0.05) };
    });
  });
  expect(samples.length).toBeGreaterThan(0);
  for (const sample of samples) expect(sample.ratio, JSON.stringify(sample)).toBeGreaterThanOrEqual(4.5);
}
