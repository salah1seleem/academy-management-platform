import { PlayerSkillJourney } from "../../../../../features/player-skills";
import { DevelopmentEntry } from "../../../../../features/development-entry";
export default async function Page({searchParams}:{searchParams:Promise<{enrollmentId?:string}>}){const {enrollmentId}=await searchParams;return enrollmentId&&/^[a-f0-9-]{36}$/i.test(enrollmentId)?<PlayerSkillJourney enrollmentId={enrollmentId}/>:<DevelopmentEntry title="رحلة المهارات" destination="/dashboard/evaluations/skills/journey"/>;}
