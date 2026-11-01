import {formatMoneyAr} from "../lib/formatters";

/** Keep the canonical formatter and full accessible amount; currency is secondary. */
export function MoneyValue({amount,currency}:{amount:number;currency:string}) {
  const formatted=formatMoneyAr(amount,currency);
  const number=new Intl.NumberFormat("ar-EG-u-nu-latn",{maximumFractionDigits:2}).format(amount);
  const index=formatted.indexOf(number);
  if(index<0)return <span>{formatted}</span>;
  return <span aria-label={formatted} style={{whiteSpace:'nowrap'}}><span aria-hidden="true">{formatted.slice(0,index)&&<small style={{fontSize:'.48em',fontWeight:500}}>{formatted.slice(0,index)}</small>}{number}<small style={{fontSize:'.48em',fontWeight:500}}>{formatted.slice(index+number.length)}</small></span></span>;
}
