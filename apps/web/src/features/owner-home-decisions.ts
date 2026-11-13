import {formatMoneyAr} from '../lib/formatters';
export type HomeDecision={title:string;line:string;action:string;href:string};
export type DecisionSources={currency:string;followups?:{overdue:number};overdue?:{amount?:number;families?:number};risk:{players:number};capacity:{seats:number};groupAttention:{id:string;name:string}[];underAssignedCoaches?:{id:string;name:string;groupCount:number;target:number}[]};
export function ownerDecisionRecords(data:DecisionSources,scope:{from:string;to:string;branchId:string}):HomeDecision[]{
 const n=(v:number)=>v.toLocaleString('ar-EG-u-nu-latn',{maximumFractionDigits:1});
 const q=new URLSearchParams(Object.entries(scope).filter(([,v])=>v));if(!scope.from&&!scope.to)q.set('allDates','true');
 const scoped=(path:string)=>`${path}${path.includes('?')?'&':'?'}${q}`;
 const amount=data.overdue?.amount, families=data.overdue?.families;
 const branch:Record<string,string>=scope.branchId?{branchId:scope.branchId}:{};
 const canRemind=typeof families==='number'&&Number.isInteger(families)&&families>0;
 const overdue=typeof amount==='number'&&Number.isFinite(amount)&&amount>0 ? [{
  title:`${formatMoneyAr(amount,data.currency)} مستحقات متأخرة`,
  line:canRemind?`أسر متاحة للتنبيه · ${n(families)}`:families===0?'لا توجد أسر متاحة للتنبيه.':'قائمة الأسر غير متاحة.',
  action:canRemind?'تحضير تذكيرات التحصيل':'مراجعة كشف المستحقات',
  href:canRemind?`/dashboard/notifications/reminders?${new URLSearchParams({kind:'Overdue',...branch})}`:`/dashboard/finance/receivables?${new URLSearchParams({state:'overdue',...branch})}`,
 }] : [];
 return [
  ...(data.followups?.overdue??0)>0?[{title:`${n(data.followups!.overdue)} متابعات تجاوزت موعدها`,line:'راجع المسؤول والموعد والإجراء التالي',action:'مراجعة المتابعات وإسنادها',href:scoped('/dashboard/operations/follow-ups?overdue=true')}]:[],
  ...overdue,
  ...data.risk.players>0?[{title:`${n(data.risk.players)} لاعب يحتاجون متابعة التجديد`,line:'راجع اللاعبين والأسر والباقات المناسبة',action:'عرض خطة الاحتفاظ بهم',href:`/dashboard/notifications/reminders?kind=Renewal&branchId=${scope.branchId}`}]:[],
  ...data.groupAttention.map(g=>({title:g.name,line:'مجموعة تحتاج مراجعة التغطية والحضور',action:'مراجعة المجموعة وفتح متابعة',href:scoped(`/dashboard/academy/groups/${g.id}`)})),
  ...(data.underAssignedCoaches??[]).map(c=>({title:`${c.name} · ${n(c.groupCount)} مجموعات من أصل ${n(c.target)}`,line:'راجع التغطية والمواعيد قبل الإسناد',action:'إسناد إلى مجموعة',href:`/dashboard/academy/coaches/new?${new URLSearchParams({coachId:c.id,...(scope.branchId?{branchId:scope.branchId}:{})})}`})),
  ...data.capacity.seats>0?[{title:`${n(data.capacity.seats)} مكان تدريب متاح`,line:'راجع المجموعات قبل استقبال تسجيلات جديدة',action:'عرض الأماكن المتاحة',href:scoped('/dashboard/operations/capacity')}]:[],
 ];
}

export type DecisionKind = "Expiring" | "Expired" | "Absence" | "Unassigned" | "Capacity";
export type SmartDecisionCounts = {
 expiring:number; expired:number; absence:number; unassigned:number; seats:number; openTasks:number;
};
export const smartDecisionLabels: Record<DecisionKind,string> = {
 Expiring:"اشتراكات تقترب من الانتهاء",
 Expired:"اشتراكات منتهية",
 Absence:"غياب ثلاث حصص متتالية",
 Unassigned:"لاعبون بانتظار التسكين",
 Capacity:"أماكن التدريب المتاحة",
};
export const decisionPath = "/dashboard/operations/decisions";
export const decisionApi = "/api/v1/manage/decision-center";

export function decisionQuery(branchId: string, kind?: DecisionKind, page?: number) {
 return new URLSearchParams({...branchId?{branchId}:{},...kind?{kind}:{},...page?{page:String(page)}:{}});
}
export function smartDecisionRecords(data: SmartDecisionCounts | undefined, branchId: string, pendingPayments: number): HomeDecision[] {
 const decision = (kind: DecisionKind) => `${decisionPath}?${decisionQuery(branchId,kind)}`;
 const n = new Intl.NumberFormat("ar-EG");
 const followUpQuery = decisionQuery(branchId);
 followUpQuery.set("allDates","true");
 const followUps = `/dashboard/operations/follow-ups?${followUpQuery}`;
 const paymentQuery = new URLSearchParams({...(branchId?{branchId}:{}),status:"Pending",allDates:"true"});
 const pending = pendingPayments > 0 ? [{
  title: "طلبات دفع بانتظار المراجعة",
  line: `الطلبات المعلّقة: ${n.format(pendingPayments)}`,action:"مراجعة طلبات الدفع",
  href:`/dashboard/subscriptions/payments?${paymentQuery}`,
 }] : [];
 if (!data) return pending;
 return [
  ...pending,
  ...data.expiring>0?[{title:smartDecisionLabels.Expiring,line:`اللاعبون: ${n.format(data.expiring)}`,action:"مراجعة اللاعبين",href:decision("Expiring")}]:[],
  ...data.expired>0?[{title:smartDecisionLabels.Expired,line:`اللاعبون: ${n.format(data.expired)}`,action:"مراجعة اللاعبين",href:decision("Expired")}]:[],
  ...data.absence>0?[{title:smartDecisionLabels.Absence,line:`اللاعبون: ${n.format(data.absence)}`,action:"عرض حالات الغياب",href:decision("Absence")}]:[],
  ...data.seats>0?[{title:smartDecisionLabels.Capacity,line:`الأماكن المتاحة: ${n.format(data.seats)}`,action:"إدارة أماكن التدريب",href:decision("Capacity")}]:[],
  ...data.unassigned>0?[{title:smartDecisionLabels.Unassigned,line:`اللاعبون: ${n.format(data.unassigned)}`,action:"تسكين اللاعبين",href:decision("Unassigned")}]:[],
  ...data.openTasks>0?[{title:"متابعات مفتوحة",line:`المهام: ${n.format(data.openTasks)}`,action:"عرض المتابعات",href:followUps}]:[],
 ];
}
