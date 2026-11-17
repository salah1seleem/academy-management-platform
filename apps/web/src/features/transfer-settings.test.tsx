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
 fireEvent.click(screen.getByRole('button',{name:'حفظ حسابات التحويل'}));
 await screen.findByText(/لم تتأكد مطابقة إعدادات التحويل المحفوظة/);
 expect(screen.queryByText('تم حفظ حسابات التحويل وتأكيد قراءتها.')).not.toBeInTheDocument();
 expect(f.reads).toBe(2);expect(f.puts).toBe(1);
 fireEvent.click(screen.getByRole('button',{name:'إعادة قراءة الإعدادات الحالية'}));
 await waitFor(()=>expect(screen.getByRole('textbox',{name:'حساب إنستا باي'})).toBeEnabled());
 expect(screen.getByRole('textbox',{name:'حساب إنستا باي'})).toHaveValue(initial.instaPayAddress);
 expect(screen.queryByRole('button',{name:'إعادة التحقق من الحفظ'})).not.toBeInTheDocument();
 expect(f.puts).toBe(1);expect(f.reads).toBe(3);
});
it('preserves the disabled state and retained payment details through save and remount',async()=>{
 const f=setup();const view=render(<TransferSettings expanded/>);
 await screen.findByRole('textbox',{name:'حساب إنستا باي'});
 expect(screen.getByRole('checkbox',{name:'تفعيل استقبال إثباتات التحويل'})).not.toBeChecked();
 fireEvent.click(screen.getByRole('button',{name:'حفظ حسابات التحويل'}));
 await screen.findByText('تم حفظ حسابات التحويل وتأكيد قراءتها.');
 expect(f.saved.isActive).toBe(false);
 view.rerender(<TransferSettings expanded key="reload"/>);
 expect(await screen.findByRole('textbox',{name:'حساب إنستا باي'})).toHaveValue(initial.instaPayAddress);
 expect(screen.getByRole('checkbox',{name:'تفعيل استقبال إثباتات التحويل'})).not.toBeChecked();
});
it('after an acknowledged save and failed read retries the read only and prevents duplicate submits',async()=>{
 const f=setup({failRead:true});render(<TransferSettings expanded/>);
 await screen.findByRole('textbox',{name:'حساب إنستا باي'});
 const button=screen.getByRole('button',{name:'حفظ حسابات التحويل'});
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
 expect(screen.queryByRole('button',{name:'حفظ حسابات التحويل'})).not.toBeInTheDocument();
 failed=false;fireEvent.click(screen.getByRole('button',{name:'إعادة تحميل إعدادات التحويل'}));
 await waitFor(()=>expect(screen.getByRole('textbox',{name:'حساب إنستا باي'})).toHaveValue(initial.instaPayAddress));
});

it.each([400, 422, 429])('keeps rejected input editable after a confirmed %s response', async status => {
 let rejected = true;
 const base = setup();
 const fetcher = global.fetch;
 global.fetch = vi.fn(async (input, init) => {
  if (String(input).endsWith('/payment-settings') && init?.method === 'PUT' && rejected) {
   return Response.json({message:'راجع حساب التحويل المدخل.'}, {status});
  }
  return fetcher(input, init);
 });
 render(<TransferSettings expanded/>);
 const account = await screen.findByRole('textbox', {name:'حساب إنستا باي'});
 fireEvent.change(account, {target:{value:'corrected@example.test'}});
 fireEvent.click(screen.getByRole('button', {name:'حفظ حسابات التحويل'}));
 expect(await screen.findByRole('alert')).toHaveTextContent('راجع حساب التحويل المدخل.');
 expect(account).toBeEnabled();
 expect(account).toHaveValue('corrected@example.test');
 expect(screen.queryByRole('button', {name:'إعادة التحقق من الحفظ'})).not.toBeInTheDocument();
 expect(base.reads).toBe(1);
 rejected = false;
 fireEvent.click(screen.getByRole('button', {name:'حفظ حسابات التحويل'}));
 await screen.findByText('تم حفظ حسابات التحويل وتأكيد قراءتها.');
 expect(base.saved.instaPayAddress).toBe('corrected@example.test');
 expect(base.puts).toBe(1);
});

it.each([401, 403])('removes transfer inputs when write access is rejected with %s', async status => {
 setup();
 const fetcher = global.fetch;
 global.fetch = vi.fn(async (input, init) => String(input).endsWith('/payment-settings') && init?.method === 'PUT'
  ? Response.json({message:'إعدادات التحويل غير متاحة لحسابك.'}, {status})
  : fetcher(input, init));
 render(<TransferSettings expanded/>);
 await screen.findByRole('textbox', {name:'حساب إنستا باي'});
 fireEvent.click(screen.getByRole('button', {name:'حفظ حسابات التحويل'}));
 expect(await screen.findByRole('alert')).toHaveTextContent('إعدادات التحويل غير متاحة لحسابك.');
 expect(screen.queryByRole('form')).not.toBeInTheDocument();
 expect(screen.queryByRole('button', {name:'إعادة التحقق من الحفظ'})).not.toBeInTheDocument();
});

it.each([{}, {configured:false,walletNumber:123}, {configured:false,isActive:'false'}])('rejects malformed read data before offering a save', async malformed => {
 let broken = true;
 global.fetch = vi.fn(async input => String(input).endsWith('/my-permissions')
  ? Response.json(['academy.settings.manage'])
  : Response.json(broken ? malformed : initial));
 render(<TransferSettings expanded/>);
 expect(await screen.findByRole('alert')).toHaveTextContent('تعذر تحميل إعدادات التحويل.');
 expect(screen.queryByRole('form')).not.toBeInTheDocument();
 broken = false;
 fireEvent.click(screen.getByRole('button', {name:'إعادة تحميل إعدادات التحويل'}));
 expect(await screen.findByRole('textbox', {name:'حساب إنستا باي'})).toHaveValue(initial.instaPayAddress);
});

it('retains an uncertain save and localizes an interrupted readback', async () => {
 const base = setup();
 const fetcher = global.fetch;
 let lost = true;
 global.fetch = vi.fn(async (input, init) => {
  if (String(input).endsWith('/payment-settings') && !init?.method && base.puts > 0 && lost) {
   throw new TypeError('Failed to fetch');
  }
  return fetcher(input, init);
 });
 render(<TransferSettings expanded/>);
 const account = await screen.findByRole('textbox', {name:'حساب إنستا باي'});
 fireEvent.click(screen.getByRole('button', {name:'حفظ حسابات التحويل'}));
 expect(await screen.findByRole('alert')).toHaveTextContent('تعذر تحميل إعدادات التحويل.');
 expect(screen.queryByText('Failed to fetch')).not.toBeInTheDocument();
 expect(account).toBeDisabled();
 lost = false;
 fireEvent.click(screen.getByRole('button', {name:'إعادة التحقق من الحفظ'}));
 await screen.findByText('تم حفظ حسابات التحويل وتأكيد قراءتها.');
 expect(base.puts).toBe(1);
});

it.each([{permissions: []}, {permissions: ['players.read']}])('does not request settings or render management controls without the required permission: $permissions', async ({permissions}) => {
 const fetcher = vi.fn<typeof fetch>().mockImplementation(async () => Response.json(permissions));
 global.fetch = fetcher;
 const view = render(<TransferSettings expanded/>);
 await waitFor(() => expect(view.container).toBeEmptyDOMElement());
 expect(fetcher).toHaveBeenCalledTimes(1);
 expect(String(fetcher.mock.calls[0][0])).toBe('/api/v1/my-permissions');
 expect(screen.queryByRole('form')).not.toBeInTheDocument();
});
