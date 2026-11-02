import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import {afterEach,beforeEach,expect,it,vi} from 'vitest';
import {ProductInventoryEditor} from './product-inventory';
const response=(data:unknown,status=200)=>new Response(JSON.stringify(data),{status});
let state={usesVariants:false,numberedUniform:false,stockRevision:0,stockQuantity:3,variants:[] as {id:string;size:string;shirtNumber:number|null;quantity:number}[]};
let writes:unknown[],reject=false;
beforeEach(()=>{state={usesVariants:false,numberedUniform:false,stockRevision:0,stockQuantity:3,variants:[]};writes=[];reject=false;global.fetch=vi.fn(async(url,init)=>{if(String(url).endsWith('/csrf'))return response({token:'csrf'});if(init?.method==='PUT'){const body=JSON.parse(String(init.body));writes.push(body);if(reject)return response({title:'تغير المخزون أثناء المراجعة. حدّث المقاسات والأرقام قبل الحفظ.'},409);state={...state,...body,usesVariants:true,stockRevision:state.stockRevision+1,variants:body.variants.map((v:object,i:number)=>({...v,id:`v${i}`}))};return response(state);}return response(state);});});
afterEach(()=>{cleanup();vi.restoreAllMocks();});
it('requires reconciliation of the legacy total and explicit review; edits invalidate the review',async()=>{
 render(<ProductInventoryEditor id="product"/>);await screen.findByLabelText('المقاس');
 const save=screen.getByRole('button',{name:'حفظ المخزون المراجع'});expect(save).toBeDisabled();
 fireEvent.change(screen.getByLabelText('المقاس'),{target:{value:'M'}});fireEvent.change(screen.getByLabelText('اللون'),{target:{value:'كحلي'}});fireEvent.click(screen.getByLabelText('راجعت الرصيد الفعلي وتوزيعه على المقاسات والأرقام.'));expect(save).toBeEnabled();
 fireEvent.change(screen.getByLabelText('الكمية'),{target:{value:'2'}});expect(save).toBeDisabled();expect(screen.getByLabelText('راجعت الرصيد الفعلي وتوزيعه على المقاسات والأرقام.')).not.toBeChecked();
 fireEvent.click(screen.getByRole('button',{name:'إضافة مقاس أو رقم'}));fireEvent.change(screen.getAllByLabelText('المقاس')[1],{target:{value:'L'}});fireEvent.change(screen.getAllByLabelText('اللون')[1],{target:{value:'أبيض'}});fireEvent.change(screen.getAllByLabelText('الكمية')[1],{target:{value:'1'}});fireEvent.click(screen.getByLabelText('راجعت الرصيد الفعلي وتوزيعه على المقاسات والأرقام.'));fireEvent.click(save);
 await screen.findByText('حُفظ رصيد كل مقاس ورقم. الإجمالي المعروض هو مجموعها.');expect(writes).toEqual([{expectedRevision:0,numberedUniform:false,reconcileExistingStock:true,variants:[{size:'M',color:'كحلي',shirtNumber:null,quantity:2},{size:'L',color:'أبيض',shirtNumber:null,quantity:1}]}]);
});
it('conflicting stock revision never reports saved, and refresh loads current balances',async()=>{
 state={usesVariants:true,numberedUniform:true,stockRevision:4,stockQuantity:1,variants:[{id:'M7',size:'M',shirtNumber:7,quantity:1}]};reject=true;
 render(<ProductInventoryEditor id="product"/>);await screen.findByLabelText('رقم القميص');expect(screen.getByLabelText('زي ذو أرقام جاهزة')).toBeDisabled();fireEvent.click(screen.getByLabelText('راجعت الرصيد الفعلي وتوزيعه على المقاسات والأرقام.'));fireEvent.click(screen.getByRole('button',{name:'حفظ المخزون المراجع'}));
 await screen.findByText('تغير المخزون أثناء المراجعة. حدّث المقاسات والأرقام قبل الحفظ.');expect(screen.queryByText('حُفظ رصيد كل مقاس ورقم. الإجمالي المعروض هو مجموعها.')).not.toBeInTheDocument();
 state={...state,stockRevision:5,stockQuantity:0,variants:[{id:'M7',size:'M',shirtNumber:7,quantity:0}]};fireEvent.click(screen.getByRole('button',{name:'تحديث المخزون'}));await waitFor(()=>expect(screen.getByLabelText('الكمية')).toHaveValue(0));expect(writes).toHaveLength(1);
});
it('changing the product clears the old reviewed inventory while the new product loads',async()=>{
 let finish:((value:Response)=>void)|undefined;
 global.fetch=vi.fn(async(url)=>String(url).includes('/second/')?new Promise<Response>(resolve=>{finish=resolve;}):response({usesVariants:true,numberedUniform:true,stockRevision:4,stockQuantity:1,variants:[{id:'M7',size:'M',shirtNumber:7,quantity:1}]}));
 const {rerender}=render(<ProductInventoryEditor id="first"/>);await screen.findByLabelText('المقاس');
 fireEvent.click(screen.getByLabelText('راجعت الرصيد الفعلي وتوزيعه على المقاسات والأرقام.'));
 expect(screen.getByRole('button',{name:'حفظ المخزون المراجع'})).toBeEnabled();
 rerender(<ProductInventoryEditor id="second"/>);
 expect(screen.queryByLabelText('المقاس')).not.toBeInTheDocument();
 finish!(response({usesVariants:true,numberedUniform:false,stockRevision:4,stockQuantity:2,variants:[{id:'L',size:'L',shirtNumber:null,quantity:2}]}));
 expect(await screen.findByLabelText('المقاس')).toHaveValue('L');
 expect(screen.getByLabelText('راجعت الرصيد الفعلي وتوزيعه على المقاسات والأرقام.')).not.toBeChecked();
});
