import Link from 'next/link';
import {MoneyValue} from '../components/money-value';
import {OperatingMonthlyChart,type OperatingMonth} from './operating-monthly-chart';
import styles from './owner-home-refinement.module.css';

type Line={key:string;label:string;amount:number;sourceUrl:string};
export type OperatingStatementPreview={currency:string;surplus:number;income:Line[];expenses:Line[];totalIncome:number;totalExpenses:number;monthly?:OperatingMonth[]|null;receivables?:number;approvedPayrollPayable?:number};
export function OwnerOperatingPanel({data,href,monthHref}:{data:OperatingStatementPreview;href:(url:string)=>string;monthHref:(month:OperatingMonth)=>string}){
 return <section className={`${styles.panel} ${styles.profit}`} aria-label="تقرير الأرباح والخسائر">
  <div className={styles.profitHead}><h2>تقرير الأرباح والخسائر</h2><Link href={href('/dashboard/finance/operating-statement')}>التقرير المفصل ←</Link></div>
  <details className={styles.cashBasis}><summary>على أساس التحصيل والصرف ⓘ</summary><p>المقبوضات والمدفوعات الفعلية بعد الاسترداد. المستحقات غير المحصلة والمصروفات والرواتب غير المسددة أرصدة منفصلة؛ الفائض النقدي ليس ربحًا نهائيًا على أساس الاستحقاق.</p></details>
  <div className={`${styles.cashSurplus} ${data.surplus<0?styles.red:styles.green}`}><span>{data.surplus<0?'العجز التشغيلي':'الفائض التشغيلي'}</span><strong><MoneyValue amount={data.surplus} currency={data.currency}/></strong></div>
  <OperatingMonthlyChart points={data.monthly??[]} currency={data.currency} href={monthHref}/>
  <div className={styles.profitColumns}>{[{title:'الإيرادات',lines:data.income,total:data.totalIncome,tone:styles.green},{title:'المصروفات',lines:data.expenses,total:data.totalExpenses,tone:styles.red}].map(s=><div key={s.title}><div className={`${styles.cashCategoryHead} ${s.tone}`}><h3>{s.title}</h3><b><MoneyValue amount={s.total} currency={data.currency}/></b></div>{s.lines.filter(l=>l.amount!==0).map(l=><Link href={href(l.sourceUrl)} key={l.key}><span>{l.label}</span><bdi><MoneyValue amount={l.amount} currency={data.currency}/></bdi></Link>)}</div>)}</div>
  {((data.receivables??0)!==0||(data.approvedPayrollPayable??0)!==0)&&<div className={styles.cashBalances}>
    {(data.receivables??0)!==0&&<Link href={href('/dashboard/finance/receivables')}><span>مستحقات لم تُحصَّل</span><b className={styles.red}><MoneyValue amount={data.receivables!} currency={data.currency}/></b></Link>}
    {(data.approvedPayrollPayable??0)!==0&&<Link href={href('/dashboard/payroll')}><span>رواتب مستحقة لم تُصرف</span><b className={styles.amber}><MoneyValue amount={data.approvedPayrollPayable!} currency={data.currency}/></b></Link>}
  </div>}
 </section>;
}
