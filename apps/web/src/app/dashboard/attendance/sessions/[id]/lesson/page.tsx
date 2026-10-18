import {SessionLesson} from "../../../../../../features/session-lessons";
export default async function Page({params}:{params:Promise<{id:string}>}){const {id}=await params;return <SessionLesson id={id}/>;}
