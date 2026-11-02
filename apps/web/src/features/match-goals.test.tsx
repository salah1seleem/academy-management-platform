import {cleanup,fireEvent,render,screen} from "@testing-library/react";
import {afterEach,expect,it} from "vitest";
import {MatchGoals} from "./match-editor";
afterEach(cleanup);
it("keeps repeated scorers, drops removed goals and adds unknown slots without inventing a player",()=>{
  const players=[{playerId:"one",name:"لاعب خيالي"}];
  const {rerender}=render(<MatchGoals score={4} players={players}/>);
  fireEvent.change(screen.getByLabelText("الهدف 1"),{target:{value:"one"}});
  fireEvent.change(screen.getByLabelText("الهدف 2"),{target:{value:"one"}});
  fireEvent.change(screen.getByLabelText("الهدف 4"),{target:{value:"OwnGoal"}});
  rerender(<MatchGoals score={2} players={players}/>);
  expect(screen.queryByLabelText("الهدف 4")).toBeNull();
  expect(screen.getByLabelText("الهدف 1")).toHaveValue("one");
  expect(screen.getByLabelText("الهدف 2")).toHaveValue("one");
  rerender(<MatchGoals score={4} players={players}/>);
  expect(screen.getByLabelText("الهدف 4")).toHaveValue("Unknown");
  expect(screen.getAllByRole("combobox")).toHaveLength(4);
});
it("zero and invalid scores produce no goal selectors",()=>{
  const {rerender}=render(<MatchGoals score={0} players={[]}/>);
  expect(screen.queryByRole("combobox")).toBeNull();
  rerender(<MatchGoals score={-2} players={[]}/>);expect(screen.queryByRole("combobox")).toBeNull();
  rerender(<MatchGoals score={2.5} players={[]}/>);expect(screen.queryByRole("combobox")).toBeNull();
});
