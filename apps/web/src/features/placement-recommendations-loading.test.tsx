import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { PlacementRecommendations } from "./placement-recommendations";

afterEach(() => { cleanup(); vi.unstubAllGlobals(); });
const readMessage = "تعذر تحميل تفضيلات التسكين. أعد المحاولة.";
const choices = { asOfDate: "2026-11-18", waitingDays: 51, choices: [{ groupId: "g", group: "مجموعة اختبارية", rank: 1, currentPlayers: 0, capacity: 2, nextDate: "2026-11-25", startTime: "11:00:00", reasons: ["مطابقة للمستوى والسعة"] }] };

it.each([
  { name: "network", fail: () => Promise.reject(new TypeError("Failed to fetch")) },
  { name: "service", fail: () => new Response(null, { status: 503 }) },
  { name: "JSON", fail: () => new Response("{invalid-json") },
  { name: "coaches", fail: () => Response.json({ coaches: [{}] }) },
])("recovers preference $name failure in Arabic before offering the form", async ({ fail }) => {
  let broken = true;
  const fetcher = vi.fn(async () => broken ? fail() : Response.json({ coaches: [{ id: "c", name: "مدرب اختباري" }] }));
  vi.stubGlobal("fetch", fetcher);
  render(<PlacementRecommendations enrollmentId="e" />);
  fireEvent.click(screen.getByText("اقتراح مجموعات مناسبة"));
  expect(await screen.findByRole("alert")).toHaveTextContent(readMessage);
  expect(screen.queryByRole("button", { name: "عرض البدائل المؤهلة" })).not.toBeInTheDocument();
  expect(screen.queryByRole("link")).not.toBeInTheDocument();
  broken = false;
  fireEvent.click(screen.getByRole("button", { name: "إعادة تحميل تفضيلات التسكين" }));
  expect(await screen.findByRole("option", { name: "مدرب اختباري" })).toBeInTheDocument();
  expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  expect(fetcher.mock.calls.length).toBe(2);
});

it.each([
  { name: "invalid JSON", fail: () => new Response("{invalid-json") },
  { name: "missing choices", fail: () => Response.json({ asOfDate: "2026-11-18", waitingDays: 51 }) },
  { name: "impossible count", fail: () => Response.json({ ...choices, choices: [{ ...choices.choices[0], currentPlayers: 3 }] }) },
])("discards a $name response and recovers the same read without assignment", async ({ fail }) => {
  let broken = true;
  const writes: string[] = [];
  vi.stubGlobal("fetch", vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = String(input);
    if (path.endsWith("/csrf")) return Response.json({ token: "test-csrf" });
    if (init?.method === "POST") { writes.push(path); return broken ? fail() : Response.json(choices); }
    return Response.json({ coaches: [] });
  }));
  render(<PlacementRecommendations enrollmentId="e" />);
  fireEvent.click(screen.getByText("اقتراح مجموعات مناسبة"));
  fireEvent.click(await screen.findByRole("button", { name: "عرض البدائل المؤهلة" }));
  expect(await screen.findByRole("alert")).toHaveTextContent("تعذر عرض بدائل التسكين. أعد المحاولة.");
  expect(screen.queryByRole("region", { name: "بدائل التسكين" })).not.toBeInTheDocument();
  broken = false;
  fireEvent.click(screen.getByRole("button", { name: "عرض البدائل المؤهلة" }));
  expect(await screen.findByRole("link", { name: /فتح المجموعة/ })).toHaveAttribute("href", "/dashboard/academy/groups/g?tab=players&candidateId=e");
  expect(writes).toEqual(["/api/v1/people/enrollments/e/placement-recommendations", "/api/v1/people/enrollments/e/placement-recommendations"]);
});
