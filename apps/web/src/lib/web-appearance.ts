"use client";
import {useSyncExternalStore} from 'react';

export type WebAppearance='dark'|'light';
const key='academy.web.appearance',changed='academy-web-appearance-changed';
let fallback:WebAppearance='dark';
function snapshot():WebAppearance {
 try{return localStorage.getItem(key)==='light'?'light':'dark';}catch{return fallback;}
}
function subscribe(listener:()=>void){
 window.addEventListener('storage',listener);window.addEventListener(changed,listener);
 return()=>{window.removeEventListener('storage',listener);window.removeEventListener(changed,listener);};
}
export function saveWebAppearance(mode:WebAppearance){
 fallback=mode;try{localStorage.setItem(key,mode);}catch{/* Keep the current choice for this page if storage is unavailable. */}
 window.dispatchEvent(new Event(changed));
}
// The server and hydration snapshot agree; the browser preference then updates
// all approved surfaces without copying external state through mount effects.
export function useWebAppearance(){return useSyncExternalStore(subscribe,snapshot,()=> 'dark' as WebAppearance);}
