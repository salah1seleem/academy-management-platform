import {formatMoneyAr} from "../lib/formatters";

/** Keep the canonical formatter and full accessible amount; currency is secondary. */
export function MoneyValue({amount,currency}:{amount:number;currency:string}) {
  const formatted=formatMoneyAr(amount,currency);
  const number=new Intl.NumberFormat("ar-EG-u-nu-latn",{maximumFractionDigits:2}).format(amount);
  const index=formatted.indexOf(number);
  if(index<0)return <span>{formatted}</span>;
  const prefix=formatted.slice(0,index).trimEnd();
  const suffix=formatted.slice(index+number.length).trimStart();
  const amountText=formatted.slice(prefix.length,formatted.length-suffix.length);
  const currencyStyle={fontSize:"max(.48em, .75rem)",fontWeight:500};
  return <span style={{whiteSpace:"nowrap"}}>{prefix&&<small style={currencyStyle}>{prefix}</small>}{amountText}{suffix&&<small style={currencyStyle}>{suffix}</small>}</span>;
}
