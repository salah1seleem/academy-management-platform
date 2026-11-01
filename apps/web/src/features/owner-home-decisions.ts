import {formatMoneyAr} from '../lib/formatters';
export type HomeDecision={title:string;line:string;action:string;href:string};
export type DecisionSources={currency:string;followups?:{overdue:number};overdue:{amount:number;families:number};risk:{players:number};capacity:{seats:number};groupAttention:{id:string;name:string}[];underAssignedCoaches?:{id:string;name:string;groupCount:number;target:number}[]};
export function ownerDecisionRecords(data:DecisionSources,scope:{from:string;to:string;branchId:string}):HomeDecision[]{
 const n=(v:number)=>v.toLocaleString('ar-EG-u-nu-latn',{maximumFractionDigits:1});
 const q=new URLSearchParams(Object.entries(scope).filter(([,v])=>v));if(!scope.from&&!scope.to)q.set('allDates','true');
 const scoped=(path:string)=>`${path}${path.includes('?')?'&':'?'}${q}`;
 return [
  ...(data.followups?.overdue??0)>0?[{title:`${n(data.followups!.overdue)} متابعات تجاوزت موعدها`,line:'راجع المسؤول والموعد والإجراء التالي',action:'مراجعة المتابعات وإسنادها',href:scoped('/dashboard/operations/follow-ups?overdue=true')}]:[],
  ...data.overdue.amount>0?[{title:`${formatMoneyAr(data.overdue.amount,data.currency)} مستحقات متأخرة`,line:`${n(data.overdue.families)} ولي أمر يحتاجون متابعة`,action:'تحضير تذكيرات التحصيل',href:`/dashboard/notifications/reminders?kind=Overdue&branchId=${scope.branchId}`}]:[],
  ...data.risk.players>0?[{title:`${n(data.risk.players)} لاعب يحتاجون متابعة التجديد`,line:'راجع اللاعبين والأسر والباقات المناسبة',action:'عرض خطة الاحتفاظ بهم',href:`/dashboard/notifications/reminders?kind=Renewal&branchId=${scope.branchId}`}]:[],
  ...data.groupAttention.map(g=>({title:g.name,line:'مجموعة تحتاج مراجعة التغطية والحضور',action:'مراجعة المجموعة وفتح متابعة',href:scoped(`/dashboard/academy/groups/${g.id}`)})),
  ...(data.underAssignedCoaches??[]).map(c=>({title:`${c.name} · ${n(c.groupCount)} مجموعات من أصل ${n(c.target)}`,line:'راجع التغطية والمواعيد قبل الإسناد',action:'إسناد إلى مجموعة',href:`/dashboard/academy/coaches/new?${new URLSearchParams({coachId:c.id,...(scope.branchId?{branchId:scope.branchId}:{})})}`})),
  ...data.capacity.seats>0?[{title:`${n(data.capacity.seats)} مكان تدريب متاح`,line:'راجع المجموعات قبل استقبال تسجيلات جديدة',action:'عرض الأماكن المتاحة',href:scoped('/dashboard/operations/capacity')}]:[],
 ];
}
