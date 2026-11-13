import {cleanup,fireEvent,render,screen} from "@testing-library/react";
import {afterEach,expect,it} from "vitest";
import {MatchResultCard} from "./match-result-card";
afterEach(cleanup);
const match={team:"فريقنا",opponent:"منافس خيالي",matchDate:"2026-10-25",startTime:"17:00",venue:"ملعب",tournament:"بطولة"};
it("uses our numeric score for outcome and keeps unfinished and missing scores neutral",()=>{
 const {rerender,container}=render(<MatchResultCard match={{...match,ourScore:4,opponentScore:2,resultFinalized:true}}/>);
 expect(screen.getByText("فوز")).toBeInTheDocument();expect(container.querySelector(".academy-match-card--win")).not.toBeNull();
 rerender(<MatchResultCard match={{...match,ourScore:0,opponentScore:0,resultFinalized:false}}/>);expect(screen.getByText("النتيجة قيد المراجعة")).toBeInTheDocument();expect(screen.queryByText("تعادل")).toBeNull();
 rerender(<MatchResultCard match={match}/>);expect(screen.getByText("مجدولة")).toBeInTheDocument();expect(screen.queryByText("تعادل")).toBeNull();
});

it("pairs both scores with their teams in reading order, including a zero", () => {
  const { container } = render(<MatchResultCard match={{ ...match, status: "Completed", ourScore: 0, opponentScore: 2 }} />);
  expect(screen.getByText("خسارة")).toBeVisible();
  const scores = screen.getAllByRole("group");
  expect(scores[0]).toHaveAccessibleName("فريقنا: 0");
  expect(scores[1]).toHaveAccessibleName("منافس خيالي: 2");
  expect(container.querySelector(".academy-match-score b")).toHaveAttribute("aria-hidden", "true");
});

it("keeps draft scores private without explicit editor preview and hides cancelled scores", () => {
  const draft = { ...match, status: "ResultDraft", resultFinalized: false, ourScore: 0, opponentScore: 2 };
  const { rerender } = render(<MatchResultCard match={draft}><a href="/fixture">عرض المباراة</a></MatchResultCard>);
  expect(screen.getByText("النتيجة قيد المراجعة")).toBeVisible();
  expect(screen.queryByLabelText("فريقنا: 0")).not.toBeInTheDocument();
  rerender(<MatchResultCard match={{ ...draft, resultFinalized: true }} />);
  expect(screen.getByText("النتيجة قيد المراجعة")).toBeVisible();
  expect(screen.queryByLabelText("فريقنا: 0")).not.toBeInTheDocument();
  rerender(<MatchResultCard match={draft} showDraftScores />);
  expect(screen.getByLabelText("فريقنا: 0")).toBeVisible();
  rerender(<MatchResultCard match={{ ...draft, status: "Cancelled", resultFinalized: true }} showDraftScores />);
  expect(screen.getByText("ملغاة")).toBeVisible();
  expect(screen.queryByLabelText("فريقنا: 0")).not.toBeInTheDocument();
});

it("retains a historical note behind a disclosure and shows active instructions immediately", () => {
  const notes = "التجمع قبل بداية المباراة بنصف ساعة.";
  const { rerender } = render(<MatchResultCard match={{ ...match, status: "Published", isUpcoming: false, notes }} />);
  expect(screen.getByText("بانتظار النتيجة")).toBeVisible();
  expect(screen.getByText(notes)).not.toBeVisible();
  fireEvent.click(screen.getByText("ملاحظات المباراة"));
  expect(screen.getByText(notes)).toBeVisible();
  rerender(<MatchResultCard match={{ ...match, status: "Published", isUpcoming: true, notes }} />);
  expect(screen.getByText(notes)).toBeVisible();
  expect(screen.queryByText("ملاحظات المباراة")).not.toBeInTheDocument();
});
