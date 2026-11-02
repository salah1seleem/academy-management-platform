import { SkillCurriculumEditor } from "../../../../../../features/player-skills";
export default async function Page({params}:{params:Promise<{id:string}>}){const {id}=await params;return <SkillCurriculumEditor key={id} id={id}/>;}
