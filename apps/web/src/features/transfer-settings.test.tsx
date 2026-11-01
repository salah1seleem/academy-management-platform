import {afterEach, expect, it, vi} from 'vitest';
import {cleanup, fireEvent, render, screen, waitFor} from '@testing-library/react';
import {TransferSettings} from './manual-payments';

afterEach(()=>{cleanup();vi.restoreAllMocks();});
const initial={instaPayAddress:'synthetic-initial@example.test',walletNumber:null,
 recipientName:'إعداد اصطناعي',configured:false,isActive:false};
function setup({mismatch=false,failRead=false}:{mismatch?:boolean;failRead?:boolean}={}){
 let saved={...initial};let puts=0;let reads=0;
 const fetcher=vi.fn(async(input:RequestInfo|URL,init?:RequestInit)=>{
  const path=String(input);
  if(path.endsWith('/my-permissions'))return Response.json(['academy.settings.manage']);
  if(path.endsWith('/auth/csrf'))return Response.json({token:'synthetic-csrf'});
  if(path.endsWith('/payment-settings')){
   if(init?.method==='PUT'){
    puts++;const b=JSON.parse(String(init.body));
    saved={...b,configured:b.isActive&&!!(b.instaPayAddress||b.walletNumber)};
    return new Response(null,{status:204});
   }
   reads++;
   if(failRead&&puts>0&&reads===2)return new Response(null,{status:503});
   return Response.json(mismatch&&puts>0?initial:saved);
  }
  throw Error('Unexpected '+path);
 });
 global.fetch=fetcher as typeof fetch;
 return {fetcher,get puts(){return puts;},get reads(){return reads;},get saved(){return saved;}};
}
it('does not announce saved transfer settings until a matching readback confirms every field and enablement',async()=>{
 const f=setup({mismatch:true});render(<TransferSettings expanded/>);
 const account=await screen.findByRole('textbox',{name:'حساب إنستا باي'});
 fireEvent.change(account,{target:{value:'synthetic-changed@example.test'}});
 fireEvent.click(screen.getByRole('button',{name:'حفظ'}));
 await screen.findByText(/لم تتأكد مطابقة إعدادات التحويل المحفوظة/);
 expect(screen.queryByText('تم حفظ حسابات التحويل وتأكيد قراءتها.')).not.toBeInTheDocument();
 expect(f.reads).toBe(2);expect(f.puts).toBe(1);
});
it('preserves the disabled state and retained payment details through save and remount',async()=>{
 const f=setup();const view=render(<TransferSettings expanded/>);
 await screen.findByRole('textbox',{name:'حساب إنستا باي'});
 expect(screen.getByRole('checkbox',{name:'تفعيل استقبال إثباتات التحويل'})).not.toBeChecked();
 fireEvent.click(screen.getByRole('button',{name:'حفظ'}));
 await screen.findByText('تم حفظ حسابات التحويل وتأكيد قراءتها.');
 expect(f.saved.isActive).toBe(false);
 view.rerender(<TransferSettings expanded key="reload"/>);
 expect(await screen.findByRole('textbox',{name:'حساب إنستا باي'})).toHaveValue(initial.instaPayAddress);
 expect(screen.getByRole('checkbox',{name:'تفعيل استقبال إثباتات التحويل'})).not.toBeChecked();
});
it('after an acknowledged save and failed read retries the read only and prevents duplicate submits',async()=>{
 const f=setup({failRead:true});render(<TransferSettings expanded/>);
 await screen.findByRole('textbox',{name:'حساب إنستا باي'});
 const button=screen.getByRole('button',{name:'حفظ'});
 fireEvent.click(button);fireEvent.click(button);
 await screen.findByRole('button',{name:'إعادة التحقق من الحفظ'});
 expect(f.puts).toBe(1);
 fireEvent.click(screen.getByRole('button',{name:'إعادة التحقق من الحفظ'}));
 await screen.findByText('تم حفظ حسابات التحويل وتأكيد قراءتها.');
 expect(f.puts).toBe(1);expect(f.reads).toBe(3);
});
it('fails closed when settings cannot be read and provides a real reload action',async()=>{
 let failed=true;
 global.fetch=vi.fn(async(input)=>String(input).endsWith('/my-permissions')
  ?Response.json(['academy.settings.manage'])
  :failed?new Response(null,{status:503}):Response.json(initial)) as typeof fetch;
 render(<TransferSettings expanded/>);
 await screen.findByText('تعذر تحميل إعدادات التحويل.');
 expect(screen.queryByRole('button',{name:'حفظ'})).not.toBeInTheDocument();
 failed=false;fireEvent.click(screen.getByRole('button',{name:'إعادة تحميل إعدادات التحويل'}));
 await waitFor(()=>expect(screen.getByRole('textbox',{name:'حساب إنستا باي'})).toHaveValue(initial.instaPayAddress));
});
