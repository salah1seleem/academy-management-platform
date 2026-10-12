import { act, cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import PlayersPage from "./page";

vi.mock("next/navigation", () => ({ useSearchParams: () => new URLSearchParams() }));
afterEach(() => { cleanup(); vi.restoreAllMocks(); });

it("keeps the latest player search when the earlier unfiltered response arrives last", async () => {
  let finishOld!: (response: Response) => void;
  const child = { id: "synthetic", playerCode: "TEST-1", arabicName: "طفل البحث الاصطناعي", dateOfBirth: "2017-01-01", enrollments: 1, isActive: true, enrollmentSummaries: [] };
  global.fetch = vi.fn(input => {
    if (String(input).includes("structure/options")) return Promise.resolve(new Response(JSON.stringify({ branches: [], sports: [], groups: [], categories: [] })));
    if (String(input).endsWith("search=")) return new Promise<Response>(resolve => { finishOld = resolve; });
    return Promise.resolve(new Response(JSON.stringify([child])));
  }) as typeof fetch;
  render(<PlayersPage />);
  await waitFor(() => expect(finishOld).toBeTypeOf("function"));
  fireEvent.change(screen.getByRole("textbox", { name: "بحث اللاعبين" }), { target: { value: child.arabicName } });
  await screen.findByText(child.arabicName, { exact: true });
  await act(async () => { finishOld(new Response(JSON.stringify([{ ...child, id: "old", arabicName: "نتيجة بحث قديمة" }]))); });
  expect(screen.getByText(child.arabicName, { exact: true })).toBeVisible();
  expect(screen.queryByText("نتيجة بحث قديمة", { exact: true })).not.toBeInTheDocument();
});
