import {PlayerMonthly} from "../../../../features/player-monthly";
export default async function Page({searchParams}:{searchParams:Promise<{enrollmentId?:string}>}){return <PlayerMonthly enrollmentId={(await searchParams).enrollmentId}/>;}
