import{PlayerBadges,BadgeRules}from"../../../../features/player-badges";
export default async function Page({searchParams}:{searchParams:Promise<{enrollmentId?:string}>}){const id=(await searchParams).enrollmentId;return id?<PlayerBadges enrollmentId={id}/>:<BadgeRules/>;}
