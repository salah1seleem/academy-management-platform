import {cleanup,fireEvent,render,screen,within} from '@testing-library/react';
import {afterEach,expect,it} from 'vitest';
import {OperatingMonthlyChart,type OperatingMonth} from './operating-monthly-chart';
import {OwnerOperatingPanel} from './owner-operating-panel';

afterEach(cleanup);
const points:OperatingMonth[]=[{from:'2026-08-12',to:'2026-08-31',income:1000,expenses:200,surplus:800},{from:'2026-09-01',to:'2026-09-28',income:-50,expenses:0,surplus:-50},{from:'2026-10-01',to:'2026-10-20',income:0,expenses:0,surplus:0}];
it('uses signed cash values with a real zero baseline, exact partial dates and no artificial bars',()=>{
 const {container}=render(<OperatingMonthlyChart points={points} currency="EGP" href={p=>`/report?from=${p.from}&to=${p.to}&branchId=b`}/>);
 const zero=Number(container.querySelector('line')!.getAttribute('y1'));
 const bars=container.querySelectorAll('rect');expect(bars).toHaveLength(3);
 expect(Number(bars[2].getAttribute('y'))).toBeCloseTo(zero);expect(Number(bars[2].getAttribute('height'))).toBeGreaterThan(0);
 expect(screen.getByRole('link',{name:/2026-08-12 — 2026-08-31/})).toHaveAttribute('href','/report?from=2026-08-12&to=2026-08-31&branchId=b');
 expect(screen.getByRole('link',{name:/2026-09-01 — 2026-09-28/})).toHaveAccessibleName(/-50/);
});
it('omits absent data rather than inventing periods',()=>{
 const {container}=render(<OperatingMonthlyChart points={[]} currency="EGP" href={()=>'/report'}/>);expect(container).toBeEmptyDOMElement();
});
it('keeps source category amounts and unpaid liabilities separate from the cash result and retains scope links',()=>{
 render(<OwnerOperatingPanel data={{currency:'EGP',surplus:650,totalIncome:950,totalExpenses:300,monthly:points,income:[{key:'sub',label:'اشتراكات محصلة',amount:1000,sourceUrl:'/receipt'},{key:'refund',label:'استردادات',amount:-50,sourceUrl:'/refund'},{key:'disabled',label:'مسابح غير مستخدمة',amount:0,sourceUrl:'/unused'}],expenses:[{key:'rent',label:'إيجار',amount:300,sourceUrl:'/expense'}],receivables:900,approvedPayrollPayable:600}} href={path=>`${path}?branchId=b`} monthHref={p=>`/monthly?from=${p.from}&to=${p.to}`}/>);
 const panel=screen.getByRole('region',{name:'تقرير الأرباح والخسائر'});
 expect(within(panel).queryByText('مسابح غير مستخدمة')).not.toBeInTheDocument();
 expect(within(panel).getByRole('link',{name:/استردادات/})).toHaveAttribute('href','/refund?branchId=b');
 expect(within(panel).getByRole('link',{name:/رواتب مستحقة لم تُصرف/})).toHaveAttribute('href','/dashboard/payroll?branchId=b');
 expect(within(panel).getByText('الفائض التشغيلي').parentElement).toHaveTextContent('650');
 fireEvent.click(within(panel).getByText('على أساس التحصيل والصرف ⓘ'));
 expect(within(panel).getByText(/ليس ربحًا نهائيًا على أساس الاستحقاق/)).toBeVisible();
});
it('preserves real positive receipts and negative refunds even when their category total nets to zero',()=>{
 render(<OwnerOperatingPanel data={{currency:'EGP',surplus:0,totalIncome:0,totalExpenses:0,income:[{key:'receipt',label:'اشتراك محصل',amount:100,sourceUrl:'/receipt'},{key:'refund',label:'استرداد حقيقي',amount:-100,sourceUrl:'/refund'}],expenses:[]}} href={p=>p} monthHref={()=>'/month'}/>);
 expect(screen.getByRole('link',{name:/اشتراك محصل/})).toHaveTextContent('100');
 expect(screen.getByRole('link',{name:/استرداد حقيقي/})).toHaveTextContent('-100');
});
