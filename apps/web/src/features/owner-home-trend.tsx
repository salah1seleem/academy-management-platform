import Link from 'next/link';
import {formatDateAr,formatMoneyAr} from '../lib/formatters';
import styles from './owner-home-trend.module.css';

export type HomeTrendPoint={from?:string;to?:string;month?:string;asOf?:string;amount?:number;count?:number};
export function OwnerHomeTrend({points,monthly=false,currency,href}:{points:HomeTrendPoint[];monthly?:boolean;currency?:string;href:(point:HomeTrendPoint)=>string}) {
  if(!points.length)return null;
  const values=points.map(p=>monthly?p.count!:p.amount!);
  const low=Math.min(0,...values),high=Math.max(0,...values),span=Math.max(1,high-low);
  // The approved time axis is chronological from left to right. Keep the
  // signed source values, including refund weeks below the zero baseline.
  const x=(i:number)=>(i+.5)*100/points.length,y=(v:number)=>4+(high-v)*56/span;
  const line=values.map((v,i)=>`${x(i)},${y(v)}`).join(' ');
  const number=(v:number)=>v.toLocaleString('ar-EG-u-nu-latn',{maximumFractionDigits:2});
  const last=monthly?points.at(-1)!.asOf:points.at(-1)!.to;
  return <div className={`${styles.trend} ${monthly?styles.monthly:''}`} role="group" aria-label={monthly?'نمو الاشتراكات':'التحصيل الأسبوعي'}>
    <h3>{monthly?'نمو الاشتراكات':'التحصيل الأسبوعي'}</h3>
    <small>
      {monthly ? <span>تسجيلات لها اشتراك · حتى {formatDateAr(last)}</span> : <span>{points.length===1?'أسبوع واحد':points.length===2?'آخر أسبوعين':`آخر ${points.length} أسابيع`} حتى {formatDateAr(last)}</span>}
      {!monthly&&currency&&<span>· {currency==='EGP'?'ج.م':currency}</span>}
    </small>
    <div className={styles.scroller}><div dir="ltr" style={{minWidth:`max(100%, ${points.length*3.25}rem)`}}>
    <svg viewBox="0 0 100 64" preserveAspectRatio="none" aria-hidden="true"><line x1="0" y1={y(0)} x2="100" y2={y(0)} className={styles.zero}/><polygon points={`${line} ${x(values.length-1)},${y(0)} ${x(0)},${y(0)}`} className={styles.area}/><polyline points={line} className={styles.line} vectorEffect="non-scaling-stroke"/>{!monthly&&<circle cx={x(values.length-1)} cy={y(values.at(-1)!)} r=".9"/>}</svg>
    <div className={styles.points} dir="ltr" style={{gridTemplateColumns:`repeat(${points.length},minmax(0,1fr))`}}>{points.map((p,i)=>{
      const date=(monthly?p.month:p.to)!;
      const label=monthly?new Intl.DateTimeFormat('ar-EG',{month:'short',timeZone:'UTC'}).format(new Date(`${date}T12:00:00Z`)):`${Number(date.slice(8,10))}/${Number(date.slice(5,7))}`;
      const detail=monthly?`${p.asOf} · ${number(values[i])} اشتراكًا`:`${formatMoneyAr(values[i],currency)} · من ${formatDateAr(p.from)} إلى ${formatDateAr(p.to)}`;
      return <Link key={date} href={href(p)} title={detail} aria-label={detail}><b dir="ltr" className={values[i]<0?styles.negative:undefined}>{number(values[i])}</b><span dir={monthly?'rtl':'ltr'}>{label}</span></Link>;
    })}</div></div></div>
  </div>;
}
