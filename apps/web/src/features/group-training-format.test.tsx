import { cleanup, fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { GroupTrainingFormat, trainingFormatName } from "./group-training-format";
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
it("requires a deliberate format and capacity review instead of guessing private capacity", async () => {
  const calls: unknown[] = []; const saved = vi.fn(); global.fetch = vi.fn(async (url, init) => String(url).endsWith("csrf") ? Response.json({ token: "t" }) : (calls.push(JSON.parse(String(init?.body))), Response.json({ registrationFormat: "Private" }))) as typeof fetch;
  render(<GroupTrainingFormat id="g" onSaved={saved} />); fireEvent.click(screen.getByText("نمط التدريب · جماعي")); fireEvent.change(screen.getByLabelText("نمط المجموعة"), { target: { value: "Private" } }); fireEvent.click(screen.getByRole("checkbox")); expect(calls).toHaveLength(0); fireEvent.submit(screen.getByRole("button").closest("form")!); await waitFor(() => expect(saved).toHaveBeenCalledOnce()); expect(calls).toEqual([{ registrationFormat: 2, capacityReviewed: true }]);
});
it("preserves failure truthfully and labels all registration types", async () => {
  const saved = vi.fn(); global.fetch = vi.fn(async url => String(url).endsWith("csrf") ? Response.json({ token: "t" }) : Response.json({ title: "المجموعة لها أعضاء" }, { status: 409 })) as typeof fetch;
  render(<GroupTrainingFormat id="g" onSaved={saved} format="SemiPrivate" />); fireEvent.click(screen.getByText("نمط التدريب · شبه خاص")); fireEvent.submit(screen.getByRole("button").closest("form")!); await screen.findByText("المجموعة لها أعضاء"); expect(saved).not.toHaveBeenCalled(); expect(trainingFormatName("Private")).toBe("خاص"); expect(trainingFormatName("Group")).toBe("جماعي");
});
