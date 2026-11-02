import Link from 'next/link';
import {formatMoneyAr} from '../lib/formatters';
import styles from './operating-monthly-chart.module.css';

export type OperatingMonth={from:string;to:string;income:number;expenses:number;surplus:number};
export function OperatingMonthlyChart({points,currency,href}:{points:OperatingMonth[];currency:string;href:(p:OperatingMonth)=>string}){
  if(!points.length)return null;
  const values=points.flatMap(p=>[p.income,p.expenses]);
  const low=Math.min(0,...values), high=Math.max(0,...values), span=Math.max(1,high-low);
  const y=(value:number)=>6+(high-value)*86/span, zero=y(0), slot=300/points.length, width=Math.min(34,slot*.22);
  const money=(v:number)=>formatMoneyAr(v,currency);
  return <figure className={styles.chart} aria-label="مقارنة المقبوض والمدفوع شهريًا">
    <figcaption><span><i className={styles.income}/>الإيرادات المحصلة</span><span><i className={styles.expenses}/>المصروفات المدفوعة</span></figcaption>
    <small dir="ltr">{points[0].from} — {points.at(-1)!.to}</small>
    <svg viewBox="0 0 300 100" preserveAspectRatio="none" aria-hidden="true"><line x1="0" x2="300" y1={zero} y2={zero}/>{points.flatMap((p,i)=>([['income',p.income,-1],['expenses',p.expenses,1]] as const).map(([kind,value,direction])=>value===0?null:<rect key={`${p.from}-${kind}`} className={styles[kind]} x={slot*(i+.5)+direction*(width+4)/2-width/2} y={Math.min(y(value),zero)} width={width} height={Math.abs(y(value)-zero)}/>))}</svg>
    <div className={styles.months} style={{gridTemplateColumns:`repeat(${points.length},minmax(0,1fr))`}}>{points.map(p=><Link key={p.from} href={href(p)} title={`${p.from} — ${p.to}، المقبوض ${money(p.income)}، المدفوع ${money(p.expenses)}`} aria-label={`${p.from} — ${p.to}، المقبوض ${money(p.income)}، المدفوع ${money(p.expenses)}`}>
      {new Intl.DateTimeFormat('ar-EG',{month:'short',year:'numeric',timeZone:'UTC'}).format(new Date(`${p.from}T12:00:00Z`))}
    </Link>)}</div>
  </figure>;
}
