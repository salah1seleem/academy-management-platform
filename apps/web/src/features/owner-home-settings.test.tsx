import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import {afterEach,expect,it,vi} from 'vitest';
import {CollectionTargetDialog,OwnerHomePreferences} from './owner-home-settings';
afterEach(()=>{cleanup();vi.restoreAllMocks();});
const json=(value:unknown,status=200)=>new Response(JSON.stringify(value),{status,headers:{'Content-Type':'application/json'}});
it('saves placement and configurable assignment target, then reloads the active revision',async()=>{
 let revision=4,home={quickActionsAtTop:false,coachAssignmentTarget:null as number|null};const writes:unknown[]=[];
 global.fetch=vi.fn(async(input,init)=>{
  const path=String(input);
  if(path.endsWith('/csrf'))return json({token:'csrf-test'});
  if(path.endsWith('/owner-home')){const body=JSON.parse(String(init!.body));writes.push(body);expect(body.expectedRevision).toBe(revision);home={quickActionsAtTop:body.quickActionsAtTop,coachAssignmentTarget:body.coachAssignmentTarget};revision++;return json({revision});}
  return json({versions:[{state:'Active',revision,settings:{ownerHome:home}}]});
 });
 const view=render(<OwnerHomePreferences/>);
 await screen.findByLabelText(/هدف المجموعات/);
 fireEvent.change(screen.getByLabelText(/هدف المجموعات/),{target:{value:'3'}});
 fireEvent.click(screen.getByRole('button',{name:'حفظ إعدادات الرئيسية'}));
 expect(await screen.findByRole('status')).toHaveTextContent('تم حفظ إعدادات الرئيسية وهدف الإسناد.');
 expect(writes).toEqual([{expectedRevision:4,quickActionsAtTop:true,coachAssignmentTarget:3}]);
 view.unmount();render(<OwnerHomePreferences/>);
 expect(await screen.findByText('الإجراءات السريعة ثابتة أعلى الرئيسية، تليها الاشتراكات والتحليلات.')).toBeVisible();
 await screen.findByLabelText(/هدف المجموعات/);
 expect(screen.getByLabelText(/هدف المجموعات/)).toHaveValue(3);
});
it('target confirmation retains branch and dates and submits the actual revision once',async()=>{
 const onSaved=vi.fn(),writes:unknown[]=[];let saved:unknown[]=[{from:'2026-09-01',to:'2026-09-30',branchId:'b1',amount:1000.25}];
 global.fetch=vi.fn(async(input,init)=>{
  const path=String(input);if(path.endsWith('/csrf'))return json({token:'csrf-test'});
  if(path.endsWith('/collection-target')){const body=JSON.parse(String(init!.body));writes.push(body);saved=[body];return json({revision:10});}
  return json({versions:[{state:'Active',revision:9,settings:{ownerHome:{targets:saved}}}]});
 });
 render(<CollectionTargetDialog scope={{from:'2026-09-01',to:'2026-09-30',branchId:'b1'}} branchLabel="فرع اصطناعي" currency="EGP" onSaved={onSaved} onClose={vi.fn()}/>);
 const input=await screen.findByLabelText('هدف الفترة (ج.م)');expect(input).toHaveValue('1000.25');
 await waitFor(()=>expect(screen.getByRole('button',{name:'حفظ الهدف'})).toBeEnabled());
 fireEvent.change(input,{target:{value:'٣٠٠٠٫٥٠'}});fireEvent.click(screen.getByRole('button',{name:'حفظ الهدف'}));
 await waitFor(()=>expect(onSaved).toHaveBeenCalledOnce());
 expect(writes).toEqual([{from:'2026-09-01',to:'2026-09-30',branchId:'b1',amount:3000.5,expectedRevision:9}]);
 expect(screen.getByRole('dialog')).toHaveTextContent('فرع اصطناعي · من 1 سبتمبر 2026 إلى 30 سبتمبر 2026');
});
it('a rejected target is not announced as saved or silently re-scoped',async()=>{
 const onSaved=vi.fn();
 let writes=0;
 global.fetch=vi.fn(async input=>{
  if(String(input).endsWith('/csrf'))return json({token:'csrf-test'});
  if(String(input).endsWith('/collection-target')){writes++;return json({},409);}
  const targets=writes===0?[]:[{from:'2026-09-01',to:'2026-09-30',branchId:null,amount:2000}];
  return json({versions:[{state:'Active',revision:writes+1,settings:{ownerHome:{targets}}}]});
 });
 render(<CollectionTargetDialog scope={{from:'2026-09-01',to:'2026-09-30',branchId:''}} branchLabel="كل الفروع" currency="EGP" onSaved={onSaved} onClose={vi.fn()}/>);
 await screen.findByLabelText('هدف الفترة (ج.م)');
 fireEvent.change(screen.getByLabelText('هدف الفترة (ج.م)'),{target:{value:'1000'}});fireEvent.click(screen.getByRole('button',{name:'حفظ الهدف'}));
 expect(await screen.findByText('تغيّرت إعدادات الأكاديمية. اقرأ الهدف الحالي قبل الحفظ.')).toBeVisible();expect(onSaved).not.toHaveBeenCalled();
 expect(screen.getByLabelText('هدف الفترة (ج.م)')).toBeDisabled();
 expect(screen.queryByRole('button',{name:'حفظ الهدف'})).not.toBeInTheDocument();
 fireEvent.click(screen.getByRole('button',{name:'قراءة الهدف الحالي'}));
 await waitFor(()=>expect(screen.getByLabelText('هدف الفترة (ج.م)')).toHaveValue('2000'));
 expect(writes).toBe(1);
});
it('does not confirm owner preferences when GET still returns the previous placement',async()=>{
 global.fetch=vi.fn(async(input,init)=>String(input).endsWith('/csrf')?json({token:'csrf-test'}):init?.method==='PUT'?json({revision:5}):json({versions:[{state:'Active',revision:4,settings:{ownerHome:{quickActionsAtTop:false,coachAssignmentTarget:null}}}]}));
 render(<OwnerHomePreferences/>);
 await screen.findByLabelText(/هدف المجموعات/);
 fireEvent.click(screen.getByRole('button',{name:'حفظ إعدادات الرئيسية'}));
 expect(await screen.findByText(/لم تتطابق إعدادات الرئيسية المقروءة/)).toBeVisible();
 expect(screen.queryByText('تم حفظ إعدادات الرئيسية وهدف الإسناد.')).not.toBeInTheDocument();
});
it('does not close target confirmation when only another branch target was persisted',async()=>{
 const onSaved=vi.fn();
 global.fetch=vi.fn(async(input,init)=>String(input).endsWith('/csrf')?json({token:'csrf-test'}):init?.method==='PUT'?json({revision:2}):json({versions:[{state:'Active',revision:2,settings:{ownerHome:{targets:[{from:'2026-09-01',to:'2026-09-30',branchId:'other',amount:3000}]}}}]}));
 render(<CollectionTargetDialog scope={{from:'2026-09-01',to:'2026-09-30',branchId:'b1'}} branchLabel="فرع اصطناعي" currency="EGP" onSaved={onSaved} onClose={vi.fn()}/>);
 await screen.findByLabelText('هدف الفترة (ج.م)');
 fireEvent.change(screen.getByLabelText('هدف الفترة (ج.م)'),{target:{value:'3000'}});
 fireEvent.click(screen.getByRole('button',{name:'حفظ الهدف'}));
 expect(await screen.findByText(/لم يتطابق هدف الفترة المقروء/)).toBeVisible();expect(onSaved).not.toHaveBeenCalled();
});
it('resolves a lost preferences response with GET only and no premature global success',async()=>{
 let top=false,writes=0;const notice=vi.fn();window.addEventListener('academy-save-success',notice);
 try {
  global.fetch=vi.fn(async(input,init)=>{
   if(String(input).endsWith('/csrf'))return json({token:'csrf-test'});
   if(init?.method==='PUT'){writes++;top=true;throw Error('network');}
   return json({versions:[{state:'Active',revision:top?5:4,settings:{ownerHome:{quickActionsAtTop:top}}}]});
  });
  render(<OwnerHomePreferences/>);await screen.findByLabelText(/هدف المجموعات/);
  fireEvent.click(screen.getByRole('button',{name:'حفظ إعدادات الرئيسية'}));
  const retry=await screen.findByRole('button',{name:'إعادة التحقق من الحفظ'});
  await waitFor(()=>expect(retry).toBeEnabled());
  expect(screen.getByLabelText(/هدف المجموعات/)).toBeDisabled();expect(notice).not.toHaveBeenCalled();
  fireEvent.click(retry);await screen.findByText('تم حفظ إعدادات الرئيسية وهدف الإسناد.');
  expect(writes).toBe(1);expect(notice).toHaveBeenCalledOnce();
 }finally{window.removeEventListener('academy-save-success',notice);}
});
it('reloads a mismatched target from the new GET snapshot without resubmitting',async()=>{
 let amount=1000,writes=0;
 global.fetch=vi.fn(async(input,init)=>{
  if(String(input).endsWith('/csrf'))return json({token:'csrf-test'});
  if(init?.method==='PUT'){writes++;return json({});}
  return json({versions:[{state:'Active',revision:5,settings:{ownerHome:{targets:[{from:'2026-09-01',to:'2026-09-30',branchId:'b1',amount}]}}}]});
 });
 render(<CollectionTargetDialog scope={{from:'2026-09-01',to:'2026-09-30',branchId:'b1'}} branchLabel="فرع اصطناعي" currency="EGP" onSaved={vi.fn()} onClose={vi.fn()}/>);
 await waitFor(()=>expect(screen.getByRole('button',{name:'حفظ الهدف'})).toBeEnabled());
 fireEvent.change(screen.getByLabelText('هدف الفترة (ج.م)'),{target:{value:'3000'}});fireEvent.click(screen.getByRole('button',{name:'حفظ الهدف'}));
 await screen.findByText(/لم يتطابق هدف الفترة المقروء/);amount=7000;
 fireEvent.click(screen.getByRole('button',{name:'قراءة الهدف الحالي'}));
 await waitFor(()=>expect(screen.getByLabelText('هدف الفترة (ج.م)')).toHaveValue('7000'));expect(writes).toBe(1);
});
it('target removal confirms absence of only the exact branch and period',async()=>{
 const onSaved=vi.fn();let targets=[{from:'2026-09-01',to:'2026-09-30',branchId:'b1',amount:1000}];
 global.fetch=vi.fn(async(input,init)=>{
  if(String(input).endsWith('/csrf'))return json({token:'csrf-test'});
  if(init?.method==='PUT'){expect(JSON.parse(String(init.body)).amount).toBeNull();targets=[{from:'2026-09-01',to:'2026-09-30',branchId:'other',amount:1000}];return json({});}
  return json({versions:[{state:'Active',revision:5,settings:{ownerHome:{targets}}}]});
 });
 render(<CollectionTargetDialog scope={{from:'2026-09-01',to:'2026-09-30',branchId:'b1'}} branchLabel="فرع اصطناعي" currency="EGP" onSaved={onSaved} onClose={vi.fn()}/>);
 await waitFor(()=>expect(screen.getByRole('button',{name:'حفظ الهدف'})).toBeEnabled());
 fireEvent.change(screen.getByLabelText('هدف الفترة (ج.م)'),{target:{value:''}});
 expect(screen.getByRole('button',{name:'حفظ الهدف'})).toBeDisabled();
 fireEvent.click(screen.getByRole('button',{name:'إزالة هدف الفترة'}));
 expect(targets).toHaveLength(1);
 fireEvent.click(screen.getByRole('button',{name:'العودة إلى التعديل'}));
 expect(targets).toHaveLength(1);
 fireEvent.click(screen.getByRole('button',{name:'إزالة هدف الفترة'}));
 fireEvent.click(screen.getByRole('button',{name:'تأكيد إزالة الهدف'}));
 await waitFor(()=>expect(onSaved).toHaveBeenCalledOnce());expect(targets).toHaveLength(1);
});

it('distinguishes the monthly default from a scoped target and disables an empty save', async()=>{
 global.fetch=vi.fn(async()=>json({versions:[{state:'Active',revision:5,settings:{monthlyCollectionTarget:5000,ownerHome:{targets:[{from:'2026-09-01',to:'2026-09-30',branchId:'other',amount:1000}]}}}]}));
 render(<CollectionTargetDialog scope={{from:'2026-09-01',to:'2026-09-30',branchId:''}} branchLabel="كل الفروع" currency="EGP" usesMonthlyDefault onSaved={vi.fn()} onClose={vi.fn()}/>);
 expect(await screen.findByLabelText('هدف الفترة (ج.م)')).toHaveValue('');
 expect(screen.getByText('الهدف الشهري الافتراضي 5,000 جنيه')).toBeVisible();
 expect(screen.getByRole('button',{name:'حفظ الهدف'})).toBeDisabled();
 expect(screen.queryByText(/لإزالة الهدف/)).toBeNull();
});


it('keeps keyboard access to recovery after a lost reply and lets the owner close safely', async()=>{
 let writes=0,reads=0;
 const onClose=vi.fn(),onSaved=vi.fn();
 global.fetch=vi.fn(async(input,init)=>{
  if(String(input).endsWith('/csrf'))return json({token:'csrf-test'});
  if(init?.method==='PUT'){writes++;throw Error('network');}
  reads++;
  if(reads>1)return json({},503);
  return json({versions:[{state:'Active',revision:7,settings:{}}]});
 });
 render(<CollectionTargetDialog scope={{from:'2026-09-01',to:'2026-09-30',branchId:''}} branchLabel="كل الفروع" currency="EGP" onSaved={onSaved} onClose={onClose}/>);
 const input=await screen.findByLabelText('هدف الفترة (ج.م)');
 fireEvent.change(input,{target:{value:'1.239'}});
 fireEvent.click(screen.getByRole('button',{name:'حفظ الهدف'}));
 expect(await screen.findByText(/بمنزلتين عشريتين/)).toBeVisible();
 expect(writes).toBe(0);
 fireEvent.change(input,{target:{value:'1000'}});
 fireEvent.click(screen.getByRole('button',{name:'حفظ الهدف'}));
 const check=await screen.findByRole('button',{name:'التحقق من الحفظ'});
 await waitFor(()=>expect(check).toHaveFocus());
 fireEvent.click(check);
 expect(await screen.findByText('تعذر تحميل إعدادات الأكاديمية.')).toBeVisible();
 await waitFor(()=>expect(check).toHaveFocus());
 expect(writes).toBe(1);
 expect(reads).toBe(2);
 fireEvent.keyDown(check,{key:'Escape'});
 expect(onClose).toHaveBeenCalledWith(true);
 expect(onSaved).not.toHaveBeenCalled();
});
