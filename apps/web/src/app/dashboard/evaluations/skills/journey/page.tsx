import { PlayerSkillJourney } from "../../../../../features/player-skills";
export default async function Page({searchParams}:{searchParams:Promise<{enrollmentId?:string}>}){const {enrollmentId}=await searchParams;return enrollmentId&&/^[a-f0-9-]{36}$/i.test(enrollmentId)?<PlayerSkillJourney enrollmentId={enrollmentId}/>:<p>افتح المهارات من الاشتراك الرياضي داخل ملف اللاعب.</p>;}
