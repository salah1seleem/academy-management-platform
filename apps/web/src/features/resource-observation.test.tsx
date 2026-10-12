import { cleanup, fireEvent, render, screen } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { ObservationDialog, ObservationSummary, type Observation } from "./resource-observation";
afterEach(() => { cleanup(); vi.restoreAllMocks(); });
const observation: Observation = { id:"o",sequence:1,count:7,expectedSnapshot:6,recordedPresentSnapshot:5,recordedTotalSnapshot:5,expectedBasis:"Roster",source:"Manual",outsideBookedHours:false,observedAtUtc:"2026-10-12T08:00:00Z",needsReview:true };
it("keeps expected, recorded and physically observed separate without accusing or marking unknown players", () => {
  render(<ObservationSummary value={observation} />); expect(screen.getByText("7 ملحوظ فعليًا")).toBeVisible(); expect(screen.getByText("متوقع: 6 · حاضر مسجل: 5")).toBeVisible(); expect(screen.getByText("فرق يحتاج تحققًا")).toBeVisible(); expect(screen.queryByRole("button")).not.toBeInTheDocument();
});
it("no observation is not a zero physical count, and a non-training booking is not an invented attendance total", () => {
  const { rerender }=render(<ObservationSummary />); expect(screen.getByText("لم يسجل عد فعلي")).toBeVisible(); rerender(<ObservationSummary value={{...observation,expectedBasis:"ReservedSeats",recordedPresentSnapshot:null}} />); expect(screen.getByText(/مقاعد محجوزة: 6 · حاضر مسجل: غير مرتبط بحصة/)).toBeVisible();
});
it("records an explicit count with one intent, refreshes board and closes the foreground dialog", async () => {
  Object.defineProperty(HTMLDialogElement.prototype,"showModal",{configurable:true,value:function(this:HTMLDialogElement){this.open=true;}}); Object.defineProperty(HTMLDialogElement.prototype,"close",{configurable:true,value:function(this:HTMLDialogElement){this.open=false;}});
  const onSaved=vi.fn(async()=>{}),onClose=vi.fn(); global.fetch=vi.fn(async (url,init)=>new Response(JSON.stringify(String(url).endsWith("/csrf")?{token:"test"}:init?.method==="POST"?observation:[]),{status:200})) as typeof fetch;
  render(<ObservationDialog booking={{id:"b",resource:"A"}} onClose={onClose} onSaved={onSaved} />); await screen.findByText("لا توجد ملاحظات محفوظة."); fireEvent.change(screen.getByLabelText("عدد المشاركين الملحوظ"),{target:{value:"7"}}); fireEvent.click(screen.getByRole("button",{name:"حفظ العد الفعلي"})); await vi.waitFor(()=>expect(onClose).toHaveBeenCalledOnce()); expect(onSaved).toHaveBeenCalledOnce(); const call=vi.mocked(fetch).mock.calls.find(([,init])=>init?.method==="POST"); expect(JSON.parse(String(call?.[1]?.body))).toEqual({count:7}); expect(new Headers(call?.[1]?.headers).get("Idempotency-Key")).toBeTruthy();
});
