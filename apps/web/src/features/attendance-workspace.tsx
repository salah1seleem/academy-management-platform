"use client";
import {useEffect,useState} from "react";
import {AttendanceHistory} from "./attendance";
import {AttendanceReport} from "./reports";
import {AttendanceCorrections} from "./attendance-corrections";
export function AttendanceWorkspace({playerId="",initialTab="history"}:{playerId?:string;initialTab?:string}){
  const [tab,setTab]=useState(initialTab),[permissions,setPermissions]=useState<string[]>([]);
  useEffect(()=>{const c=new AbortController();fetch("/api/v1/my-permissions",{signal:c.signal,cache:"no-store"}).then(r=>r.ok?r.json():[]).then(setPermissions).catch(()=>{});return()=>c.abort();},[]);
  const canReport=permissions.includes("reports.attendance.read");
  return <><nav className="workspace-tabs" aria-label="سجل الحضور وأدواته"><button aria-current={tab==="history"?"page":undefined} onClick={()=>setTab("history")}>سجل اللاعبين</button>{canReport&&!playerId&&<button aria-current={tab==="reports"?"page":undefined} onClick={()=>setTab("reports")}>التحليل والتصدير</button>}<button aria-current={tab==="corrections"?"page":undefined} onClick={()=>setTab("corrections")}>طلبات تصحيح الحضور</button></nav>{tab==="corrections"?<AttendanceCorrections/>:tab==="reports"&&canReport&&!playerId?<AttendanceReport/>:<AttendanceHistory playerId={playerId}/>}</>;
}
