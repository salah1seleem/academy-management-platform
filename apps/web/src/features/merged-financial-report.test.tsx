import {cleanup,fireEvent,render,screen,waitFor} from '@testing-library/react';
import {afterEach,beforeEach,describe,expect,it,vi} from 'vitest';
import {FinancialReport,FinancialSourceTiles} from './reports';
let scope='from=2026-09-21&to=2026-10-21&branchId=b1';
vi.mock('next/navigation',()=>({useSearchParams:()=>new URLSearchParams(scope)}));
vi.mock('../components/date-filter-defaults',()=>({useDateFilterDefaults:()=>({from:'2026-09-21',to:'2026-10-21'})}));
const sources=[{key:'Subscription',label:'اشتراكات اختبار',amount:1000},{key:'Refund',label:'استرداد اختبار',amount:-50},{key:'Product',label:'منتجات اختبار',amount:0}];
describe('Merged collection ledger',()=>{
 beforeEach(()=>{localStorage.clear();scope='from=2026-09-21&to=2026-10-21&branchId=b1';});afterEach(()=>{cleanup();vi.restoreAllMocks();});
 it('only real nonzero sources appear, refund stays negative, and source selection is explicit',()=>{
   const select=vi.fn();render(<FinancialSourceTiles sources={sources} currency="EGP" selected="" onSelect={select}/>);
   expect(screen.queryByRole('button',{name:/منتجات اختبار/})).not.toBeInTheDocument();
   expect(screen.getByRole('button',{name:/استرداد اختبار/})).toHaveTextContent('-50');
   fireEvent.click(screen.getByRole('button',{name:/اشتراكات اختبار/}));expect(select).toHaveBeenCalledWith('Subscription');
 });
 it('summary, ledger paging and export retain the same applied branch/date scope',async()=>{
   global.fetch=vi.fn(async(input)=>{
     const path=String(input);
     if(path.includes('structure/options'))return Response.json({branches:[{id:'b1',arabicName:'فرع أول'}],sports:[],groups:[]});
     if(path.includes('subscriptions/plans'))return Response.json([]);
     const page=path.includes('page=2')?2:1;
     return Response.json({currency:'EGP',totalAmount:1000,totalRefunds:50,netAmount:950,totalCount:26,page,pageSize:25,sourceSummary:sources,items:[{collectionId:'c'+page,receiptId:'r'+page,receiptNumber:'LEDGER-'+page,player:'لاعب اصطناعي '+page,amount:1000,netAmount:950,currency:'EGP',confirmedAtUtc:'2026-10-21T10:00:00Z',collectionType:'Subscription'}]});
   }) as typeof fetch;
   render(<FinancialReport/>);await screen.findByText('LEDGER-1');
   expect(screen.getByRole('button',{name:/اشتراكات اختبار/})).toHaveTextContent('1,000');
   fireEvent.click(screen.getByRole('button',{name:'التالي'}));await screen.findByText('LEDGER-2');
   expect(screen.getByRole('button',{name:/اشتراكات اختبار/})).toHaveTextContent('1,000');
   expect(vi.mocked(fetch).mock.calls.some(([path])=>String(path).includes('page=2')&&String(path).includes('branchId=b1')&&String(path).includes('from=2026-09-21'))).toBe(true);
   fireEvent.change(screen.getByLabelText('من تاريخ'),{target:{value:'2026-10-01'}});
   expect(screen.getByRole('link',{name:'تصدير CSV'})).toHaveAttribute('href',expect.stringContaining('from=2026-09-21'));
   fireEvent.click(screen.getByRole('button',{name:'تطبيق الفلاتر'}));await waitFor(()=>expect(screen.getByRole('link',{name:'تصدير CSV'})).toHaveAttribute('href',expect.stringContaining('from=2026-10-01')));
   expect(vi.mocked(fetch).mock.calls.filter(([path])=>String(path).includes('operating-statement'))).toHaveLength(0);
   fireEvent.click(screen.getByRole('button',{name:'مسح'}));
   await waitFor(()=>expect(screen.getByRole('link',{name:'تصدير CSV'})).toHaveAttribute('href','/api/v1/reports/financial/export?allDates=true'));
   await screen.findByText('LEDGER-1');
 });
});

it('a changed incoming branch removes the previous ledger immediately while the new read is pending',async()=>{
 let release!:(r:Response)=>void;
 const record=(branch:string)=>({currency:'EGP',totalAmount:900,totalCount:1,items:[{collectionId:'c'+branch,receiptId:'r'+branch,receiptNumber:'LEDGER-'+branch,player:'لاعب اصطناعي '+branch,amount:900,currency:'EGP',confirmedAtUtc:'2026-10-21T10:00:00Z'}]});
 global.fetch=vi.fn(async(input)=>{const p=String(input);if(p.includes('structure/options'))return Response.json({branches:[],sports:[],groups:[]});if(p.includes('subscriptions/plans'))return Response.json([]);if(p.includes('branchId=b2'))return await new Promise<Response>(r=>{release=r;});return Response.json(record('b1'));}) as typeof fetch;
 const view=render(<FinancialReport/>);await screen.findByText('LEDGER-b1');
 scope='from=2026-09-21&to=2026-10-21&branchId=b2';view.rerender(<FinancialReport/>);
 expect(screen.queryByText('LEDGER-b1')).not.toBeInTheDocument();
 expect(screen.queryByText('لاعب اصطناعي b1')).not.toBeInTheDocument();
 release(Response.json(record('b2')));await screen.findByText('LEDGER-b2');
 expect(screen.getByRole('link',{name:'تصدير CSV'})).toHaveAttribute('href',expect.stringContaining('branchId=b2'));
});
