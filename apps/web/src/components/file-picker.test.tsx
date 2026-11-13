import {cleanup,fireEvent,render,screen} from "@testing-library/react";
import {afterEach,expect,it,vi} from "vitest";
import {FilePicker} from "./file-picker";
afterEach(cleanup);
it("keeps the native field, delegates the selection, and clears its caption on form reset",async()=>{
  const change=vi.fn(),file=new File(["synthetic demo"],"صورة-تجريبية.png",{type:"image/png"});
  render(<form><label>صورة اللاعب<FilePicker aria-label="صورة اللاعب" name="photo" accept="image/png" onChange={change}/></label></form>);
  const input=screen.getByLabelText("صورة اللاعب");
  expect(input).toHaveAttribute("type","file");expect(input).toHaveAttribute("name","photo");
  expect(screen.getByText("لم يتم اختيار ملف")).toBeVisible();
  fireEvent.change(input,{target:{files:[file]}});
  expect(screen.getByText("صورة-تجريبية.png")).toBeVisible();
  expect(change).toHaveBeenCalledOnce();
  expect((input as HTMLInputElement).files?.[0]).toBe(file);
  (input as HTMLInputElement).form?.reset();
  expect(await screen.findByText("لم يتم اختيار ملف")).toBeVisible();
  expect(screen.queryByText("صورة-تجريبية.png")).not.toBeInTheDocument();
});
it("preserves a disabled required file field while a form is busy",()=>{
  render(<label>إثبات التحويل<FilePicker aria-label="إثبات التحويل" name="proof" required disabled/></label>);
  expect(screen.getByLabelText("إثبات التحويل")).toBeDisabled();
  expect(screen.getByLabelText("إثبات التحويل")).toBeRequired();
});
