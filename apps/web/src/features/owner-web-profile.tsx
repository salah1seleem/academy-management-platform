"use client";
import {useState} from 'react';
import {UserRound} from 'lucide-react';
import {csrfRequest} from './dashboard-api';
export function OwnerWebProfile({name,mode,onMode}:{name:string;mode:'dark'|'light';onMode:(m:'dark'|'light')=>void}){
 const [open,setOpen]=useState(false),[error,setError]=useState(''),[busy,setBusy]=useState(false);
 async function logout(){if(busy)return;setBusy(true);try{await csrfRequest('/api/v1/auth/logout','POST',{}, {},{announce:false});
 // Full navigation discards authenticated page state after logout.
 // eslint-disable-next-line @next/next/no-location-assign-relative-destination
 window.location.assign('/login');}catch{setError('تعذر تأكيد تسجيل الخروج. حاول مجددًا.');setBusy(false);}}
 return <div className="approved-web-profile"><button aria-label="حسابي" aria-expanded={open} onClick={()=>setOpen(!open)}><UserRound size={19}/></button>{open&&<section role="dialog" aria-label="الملف الشخصي" onKeyDown={e=>{if(e.key==='Escape')setOpen(false);}}><strong>{name||'حساب المالك'}</strong><fieldset><legend>المظهر</legend><button aria-pressed={mode==='dark'} onClick={()=>onMode('dark')}>داكن</button><button aria-pressed={mode==='light'} onClick={()=>onMode('light')}>فاتح</button></fieldset><p>اللغة · العربية</p>{error&&<p role="alert">{error}</p>}<button disabled={busy} onClick={()=>void logout()}>تسجيل الخروج</button><button onClick={()=>setOpen(false)}>إغلاق</button></section>}</div>;
}
