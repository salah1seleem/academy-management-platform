import {cleanup,render,screen} from "@testing-library/react";
import {afterEach,expect,it} from "vitest";
import {MatchResultCard} from "./match-result-card";
afterEach(cleanup);
const match={team:"فريقنا",opponent:"منافس خيالي",matchDate:"2026-10-25",startTime:"17:00",venue:"ملعب",tournament:"بطولة"};
it("uses our numeric score for outcome and keeps unfinished and missing scores neutral",()=>{
 const {rerender,container}=render(<MatchResultCard match={{...match,ourScore:4,opponentScore:2,resultFinalized:true}}/>);
 expect(screen.getByText("فوز")).toBeInTheDocument();expect(container.querySelector(".academy-match-card--win")).not.toBeNull();
 rerender(<MatchResultCard match={{...match,ourScore:0,opponentScore:0,resultFinalized:false}}/>);expect(screen.getByText("النتيجة قيد المراجعة")).toBeInTheDocument();expect(screen.queryByText("تعادل")).toBeNull();
 rerender(<MatchResultCard match={match}/>);expect(screen.getByText("قادمة")).toBeInTheDocument();expect(screen.queryByText("تعادل")).toBeNull();
});
