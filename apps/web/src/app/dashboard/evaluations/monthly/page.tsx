import {PlayerMonthly} from "../../../../features/player-monthly";
import {DevelopmentEntry} from "../../../../features/development-entry";
export default async function Page({searchParams}:{searchParams:Promise<{enrollmentId?:string}>}){const {enrollmentId}=await searchParams;return enrollmentId&&/^[a-f0-9-]{36}$/i.test(enrollmentId)?<PlayerMonthly enrollmentId={enrollmentId}/>:<DevelopmentEntry title="تقرير التطور الشهري" destination="/dashboard/evaluations/monthly"/>;}
