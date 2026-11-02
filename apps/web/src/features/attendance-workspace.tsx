"use client";
import {useEffect,useState} from "react";
import {AttendanceHistory} from "./attendance";
import {AttendanceReport} from "./reports";
import {AttendanceCorrections} from "./attendance-corrections";
import {LoadingState,PageHeader} from "../components/dashboard-shell";
export function AttendanceWorkspace({playerId="",initialTab="history"}:{playerId?:string;initialTab?:string}){
  const [tab,setTab]=useState(initialTab),[permissions,setPermissions]=useState<string[]|null>(null);
  useEffect(()=>{const c=new AbortController();fetch("/api/v1/my-permissions",{signal:c.signal,cache:"no-store"}).then(r=>r.ok?r.json():[]).then(value=>{if(!c.signal.aborted)setPermissions(value);}).catch(()=>{if(!c.signal.aborted)setPermissions([]);});return()=>c.abort();},[]);
  const canReport=permissions?.includes("reports.attendance.read");
  return <><nav className="workspace-tabs" aria-label="سجل الحضور وأدواته"><button aria-current={tab==="history"?"page":undefined} onClick={()=>setTab("history")}>سجل اللاعبين</button>{canReport&&!playerId&&<button aria-current={tab==="reports"?"page":undefined} onClick={()=>setTab("reports")}>التحليل والتصدير</button>}<button aria-current={tab==="corrections"?"page":undefined} onClick={()=>setTab("corrections")}>طلبات تصحيح الحضور</button></nav>{tab==="corrections"?<AttendanceCorrections/>:tab==="reports"&&!playerId&&permissions===null?<><PageHeader title="تقارير الحضور" context="التقارير / الحضور الفعلي"/><LoadingState/></>:tab==="reports"&&canReport&&!playerId?<AttendanceReport/>:<AttendanceHistory playerId={playerId}/>}</>;
}
