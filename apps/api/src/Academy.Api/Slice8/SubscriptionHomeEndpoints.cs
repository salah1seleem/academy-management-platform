using System.Linq.Expressions;
using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;

namespace Academy.Api.Slice8;
public static class SubscriptionHomeEndpoints
{
    public static void MapSubscriptionHome(this WebApplication app)
    {
        var api=app.MapGroup("/api/v1/subscriptions/home").RequireAuthorization(AcademyPermissions.SubscriptionRead);
        api.MapPost("/expiry-notices",async (ExpiryReminderWrite body,CurrentTenant tenant,FoundationDbContext db,TimeProvider clock)=>{
            var t=(await tenant.ResolveAsync())!;if(t.Role!=AcademyRole.AcademyOwner)return Results.Forbid();
            if(body.BranchId.HasValue&&!await db.Branches.AnyAsync(b=>b.Id==body.BranchId&&b.IsActive)||body.SportId.HasValue&&!await db.Sports.AnyAsync(s=>s.Id==body.SportId&&s.IsActive))return Results.NotFound();
            if(await db.NoticeTemplates.AnyAsync(n=>n.Key=="Expiring"&&!n.IsActive))return Results.UnprocessableEntity(new{message="تنبيه قرب الانتهاء موقوف في إعدادات الإشعارات."});
            var periods=0;
            for(var i=0;i<10;i++){var count=await Academy.Api.Operations.NotificationSchedule.ProcessAcademy(db,t.AcademyId,clock.GetUtcNow(),expiringOnly:true,branchId:body.BranchId,sportId:body.SportId);periods+=count;if(count<100)break;}
            return Results.Ok(new{periods,message=periods==0?"لا توجد تنبيهات جديدة؛ أُرسلت التنبيهات المستحقة سابقًا أو لا توجد اشتراكات مؤهلة.":$"أُرسلت تنبيهات داخل التطبيق لأسر {periods} اشتراكًا يقترب من الانتهاء."});
        }).AddEndpointFilter<CsrfFilter>();
        api.MapGet("",Overview);api.MapGet("/breakdown",Breakdown);api.MapGet("/items",Items);
    }
    internal static IQueryable<SubscriptionPeriod> Periods(FoundationDbContext db,Guid academy,DateOnly date,Guid? branch,Guid? sport=null,DateOnly? from=null)=>db.SubscriptionPeriods.AsNoTracking().Where(p=>p.AcademyId==academy&&p.SportEnrollment.IsActive&&p.SportEnrollment.Player.IsActive&&(!branch.HasValue||p.SportEnrollment.BranchId==branch)&&(!sport.HasValue||p.SportEnrollment.SportId==sport)&&p.Status!=SubscriptionPeriodStatus.Cancelled&&p.StartDate<=date&&(!from.HasValue||p.EndDate==null||p.EndDate>=from));
    internal static IQueryable<SubscriptionPeriod> Active(IQueryable<SubscriptionPeriod> periods,DateOnly date)=>periods.Where(p=>p.Status!=SubscriptionPeriodStatus.Frozen&&(p.EndDate==null||p.EndDate>=date)&&((p.PlanTypeSnapshot??p.SubscriptionPlan.PlanType)==SubscriptionPlanType.Duration||p.RemainingSessions>0));
    internal static IQueryable<SubscriptionPeriod> Select(IQueryable<SubscriptionPeriod> total,DateOnly date,string? state)
    {
        var active=Active(total,date);
        return state switch{
            "active"=>active,
            "current"=>active.Where(p=>!active.Any(n=>n.SportEnrollmentId==p.SportEnrollmentId&&n.EndDate<=date.AddDays(7))),
            "expiring"=>active.Where(p=>p.EndDate<=date.AddDays(7)),
            "expired"=>total.Where(p=>p.Status!=SubscriptionPeriodStatus.Frozen&&(p.EndDate<date||(p.PlanTypeSnapshot??p.SubscriptionPlan.PlanType)!=SubscriptionPlanType.Duration&&p.RemainingSessions<=0)&&!active.Any(a=>a.SportEnrollmentId==p.SportEnrollmentId)&&!total.Any(f=>f.SportEnrollmentId==p.SportEnrollmentId&&f.Status==SubscriptionPeriodStatus.Frozen)),
            _=>total
        };
    }
    private sealed class Snapshot
    {
        public Guid Id {get;set;}
        public Guid PlayerId {get;set;}
        public Guid BranchId {get;set;}
        public Guid SportId {get;set;}
        public Guid? GroupId {get;set;}
        public Guid PlanId {get;set;}
        public Guid PeriodId {get;set;}
    }
    private static IQueryable<Snapshot> Rows(FoundationDbContext db,Guid academy,DateOnly date,Guid? branch,Guid? sport,string? state,DateOnly? from=null)
    {
        var periods=Periods(db,academy,date,branch,sport,from);var ids=Select(periods,date,state).Select(p=>p.SportEnrollmentId).Distinct();
        var matching=Select(periods,date,state);
        return db.SportEnrollments.Where(e=>e.AcademyId==academy&&ids.Contains(e.Id)).Select(e=>new{e,period=matching.Where(p=>p.SportEnrollmentId==e.Id).OrderByDescending(p=>p.Status!=SubscriptionPeriodStatus.Frozen&&(p.EndDate==null||p.EndDate>=date)&&((p.PlanTypeSnapshot??p.SubscriptionPlan.PlanType)==SubscriptionPlanType.Duration||p.RemainingSessions>0)).ThenByDescending(p=>p.Status==SubscriptionPeriodStatus.Frozen).ThenByDescending(p=>p.StartDate).ThenByDescending(p=>p.Id).First()}).Select(x=>new Snapshot{
            Id=x.e.Id,PlayerId=x.e.PlayerId,BranchId=x.e.BranchId,SportId=x.e.SportId,GroupId=FoundationDbContext.AssignedGroupAt(x.e.AcademyId,x.e.Id,date),PlanId=x.period.SubscriptionPlanId,PeriodId=x.period.Id
        });
    }
    private static bool ValidState(string? state)=>state is null or "" or "all" or "subscribed" or "active" or "current" or "expiring" or "expired";
    private static bool ValidDimension(string? dimension)=>dimension is "branches" or "sports" or "packages" or "groups" or "days" or "ages" or "months";
    private static async Task<bool> ValidScope(FoundationDbContext db,TenantMembership t,Guid? branch,Guid? sport)=>t.Role is AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin
        &&(!branch.HasValue||await db.Branches.AnyAsync(b=>b.AcademyId==t.AcademyId&&b.Id==branch&&b.IsActive))&&(!sport.HasValue||await db.Sports.AnyAsync(s=>s.AcademyId==t.AcademyId&&s.Id==sport&&s.IsActive));
    private static async Task<IResult> Overview(DateOnly? asOf,DateOnly? from,DateOnly? to,Guid? branchId,Guid? sportId,CurrentTenant tenant,FoundationDbContext db,TimeProvider time,HttpResponse response)
    {
        var t=(await tenant.ResolveAsync())!;return await ReadOverview(t,db,time,response,to??asOf,branchId,sportId,from);
    }
    internal static async Task<IResult> ReadOverview(TenantMembership t,FoundationDbContext db,TimeProvider time,HttpResponse response,DateOnly? asOf=null,Guid? branchId=null,Guid? sportId=null,DateOnly? from=null)
    {
        if(!await ValidScope(db,t,branchId,sportId))return Results.NotFound();var date=asOf??await AttendanceCalendar.TodayAsync(t.AcademyId,db,time);if(date.Year is <2000 or >2100||from.HasValue&&(from>date||from.Value.Year<2000))return Results.UnprocessableEntity(new{message="اختر تاريخًا صحيحًا للاشتراكات."});response.Headers.CacheControl="private, no-store";
        var periods=Periods(db,t.AcademyId,date,branchId,sportId,from);var enrolled=Rows(db,t.AcademyId,date,branchId,sportId,null,from);var dimensions=new List<string>();if(await db.Branches.CountAsync(b=>b.IsActive)>1)dimensions.Add("branches");if(await db.Sports.CountAsync(s=>s.IsActive)>1)dimensions.Add("sports");dimensions.AddRange(["packages","groups","days","ages","months"]);
        var firstSubscriptionDates = Periods(db,t.AcademyId,date,branchId,sportId)
            .GroupBy(p=>p.SportEnrollmentId).Select(g=>g.Min(p=>p.StartDate));
        var newSubscriptions = from.HasValue ? await firstSubscriptionDates.CountAsync(d=>d>=from.Value) : (int?)null;
        return Results.Ok(new{asOf=date,from,to=date,newSubscriptions,total=await periods.Select(p=>p.SportEnrollmentId).Distinct().CountAsync(),active=await Active(periods,date).Select(p=>p.SportEnrollmentId).Distinct().CountAsync(),expiring=await Select(periods,date,"expiring").Select(p=>p.SportEnrollmentId).Distinct().CountAsync(),expired=await Select(periods,date,"expired").Select(p=>p.SportEnrollmentId).Distinct().CountAsync(),players=await enrolled.Select(r=>r.PlayerId).Distinct().CountAsync(),periodRecords=await periods.CountAsync(),dimensions,unit="SportEnrollmentsWithSubscriptions",nearExpiryDays=7,expiringIncludedInActive=true,academy=t.AcademyName,scope=t.Role==AcademyRole.AcademyAdmin?"الفروع والرياضات المصرح بها":"الأكاديمية",branches=await db.Branches.Where(b=>b.IsActive).OrderBy(b=>b.ArabicName).Select(b=>new{b.Id,name=b.ArabicName}).ToArrayAsync(),sports=await db.Sports.Where(s=>s.IsActive).OrderBy(s=>s.ArabicName).Select(s=>new{s.Id,name=s.ArabicName,kind=s.Kind.ToString()}).ToArrayAsync()});
    }
    private sealed record Count(Guid? Key,int Total);
    private static Task<Count[]> Counts(IQueryable<Snapshot> rows,Expression<Func<Snapshot,Guid?>> key)=>rows.GroupBy(key).Select(g=>new Count(g.Key,g.Count())).ToArrayAsync();
    private sealed record Slice(string Key,string Name,int Total,string? Detail=null);
    internal sealed record TrendPoint(DateOnly Month,DateOnly AsOf,int Count);
    internal static async Task<TrendPoint[]> Trend(FoundationDbContext db,Guid academy,DateOnly end,Guid? branch,Guid? sport=null) {
        var first=new DateOnly(end.Year,end.Month,1).AddMonths(-5);var dates=Enumerable.Range(0,6).Select(i=>i==5?end:first.AddMonths(i+1).AddDays(-1)).ToArray();var d0=dates[0];var d1=dates[1];var d2=dates[2];var d3=dates[3];var d4=dates[4];var d5=dates[5];
        var source=Periods(db,academy,end,branch,sport).GroupBy(p=>p.SportEnrollmentId).Select(g=>g.Min(p=>p.StartDate));var counts=await source.GroupBy(_=>1).Select(g=>new{c0=g.Count(d=>d<=d0),c1=g.Count(d=>d<=d1),c2=g.Count(d=>d<=d2),c3=g.Count(d=>d<=d3),c4=g.Count(d=>d<=d4),c5=g.Count(d=>d<=d5)}).SingleOrDefaultAsync();var values=counts==null?new int[6]:new[]{counts.c0,counts.c1,counts.c2,counts.c3,counts.c4,counts.c5};return Enumerable.Range(0,6).Select(i=>new TrendPoint(first.AddMonths(i),dates[i],values[i])).Where(p=>p.AsOf.Year>=2000).ToArray();
    }
    private static async Task<Dictionary<Guid,string>> DayPatterns(FoundationDbContext db,Guid[] groups,DateOnly date)
    {
        var schedules=await db.RecurringSchedules.Where(s=>s.IsActive&&groups.Contains(s.TrainingGroupId)&&(s.ValidFrom==null||s.ValidFrom<=date)&&(s.ValidTo==null||s.ValidTo>=date)).Select(s=>new{s.TrainingGroupId,s.DayOfWeek}).ToArrayAsync();
        return schedules.GroupBy(s=>s.TrainingGroupId).ToDictionary(g=>g.Key,g=>string.Join(",",g.Select(s=>(int)s.DayOfWeek).Distinct().Order()));
    }
    private static string DayName(string pattern)=>pattern=="none"?"دون مواعيد أسبوعية محددة":string.Join(" · ",pattern.Split(',').Select(n=>new[]{"الأحد","الاثنين","الثلاثاء","الأربعاء","الخميس","الجمعة","السبت"}[int.Parse(n)]));
    private static async Task<Slice[]> Slices(string dimension,IQueryable<Snapshot> rows,FoundationDbContext db,DateOnly date)
    {
        if(dimension=="days"){
            var groups=await Counts(rows,r=>r.GroupId);var patterns=await DayPatterns(db,groups.Where(g=>g.Key.HasValue).Select(g=>g.Key!.Value).ToArray(),date);
            return groups.GroupBy(g=>g.Key.HasValue?patterns.GetValueOrDefault(g.Key.Value,"none"):"none").Select(g=>new Slice(g.Key,DayName(g.Key),g.Sum(r=>r.Total),"تسجيلات حسب مجموعة الأيام، دون عدّ كل حصة مؤرخة كعضو جديد")).OrderByDescending(r=>r.Total).ThenBy(r=>r.Key).ToArray();
        }
        var counts=await Counts(rows,dimension switch{"branches"=>r=>(Guid?)r.BranchId,"sports"=>r=>(Guid?)r.SportId,"packages"=>r=>(Guid?)r.PlanId,_=>r=>r.GroupId});
        if(dimension=="ages"){
            var ids=counts.Where(c=>c.Key.HasValue).Select(c=>c.Key!.Value).ToArray();var groups=await db.TrainingGroups.Where(g=>ids.Contains(g.Id)).Select(g=>new{g.Id,g.AgeCategoryId,name=g.AgeCategory.ArabicName}).ToArrayAsync();
            return counts.GroupBy(c=>groups.SingleOrDefault(g=>g.Id==c.Key)?.AgeCategoryId).Select(g=>new Slice(g.Key?.ToString()??"none",groups.FirstOrDefault(x=>x.AgeCategoryId==g.Key)?.name??"دون فئة مجموعة مسجلة",g.Sum(c=>c.Total))).OrderByDescending(g=>g.Total).ThenBy(g=>g.Key).ToArray();
        }
        if(dimension=="packages"){
            var periodIds=rows.Select(r=>r.PeriodId);
            var variants=await db.SubscriptionPeriods.Where(p=>periodIds.Contains(p.Id)).GroupBy(p=>new{p.SubscriptionPlanId,name=p.PlanNameSnapshot??p.SubscriptionPlan.ArabicName,type=p.PlanTypeSnapshot??p.SubscriptionPlan.PlanType,sessions=p.SessionCountSnapshot??p.SubscriptionPlan.SessionCount,format=p.TrainingFormatSnapshot??p.SubscriptionPlan.TrainingFormat,days=p.DurationDaysSnapshot??p.SubscriptionPlan.DurationDays}).Select(g=>g.Key).ToArrayAsync();
            return counts.Select(c=>{var terms=variants.Where(v=>v.SubscriptionPlanId==c.Key).ToArray();var v=terms.FirstOrDefault();return new Slice(c.Key!.Value.ToString(),v==null?"باقة غير متاحة":v.name,c.Total,terms.Length!=1?"شروط محفوظة متعددة؛ راجع تفاصيل الاشتراكات":(v!.type==SubscriptionPlanType.Sessions?"حصص":v.type==SubscriptionPlanType.Combined?"مدة وحصص":"مدة")+(v.sessions.HasValue?" · "+v.sessions+" حصة":"")+(v.days.HasValue?" · "+v.days+" يوم":"")+" · "+(v.format==TrainingFormat.Private?"خاص":v.format==TrainingFormat.SemiPrivate?"شبه خاص":v.format==TrainingFormat.Season?"موسم":"جماعي"));}).OrderByDescending(c=>c.Total).ThenBy(c=>c.Key).ToArray();
        }
        var keys=counts.Where(c=>c.Key.HasValue).Select(c=>c.Key!.Value).ToArray();Dictionary<Guid,string> names=dimension switch{
            "branches"=>await db.Branches.Where(b=>keys.Contains(b.Id)).ToDictionaryAsync(b=>b.Id,b=>b.ArabicName),
            "sports"=>await db.Sports.Where(s=>keys.Contains(s.Id)).ToDictionaryAsync(s=>s.Id,s=>s.ArabicName),
            "packages"=>await db.SubscriptionPlans.Where(p=>keys.Contains(p.Id)).ToDictionaryAsync(p=>p.Id,p=>p.ArabicName+" · "+(p.PlanType==SubscriptionPlanType.Sessions?"حصص":p.PlanType==SubscriptionPlanType.Combined?"مدة وحصص":"مدة")+(p.SessionCount==null?"":" · "+p.SessionCount+" حصة")+" · "+(p.TrainingFormat==TrainingFormat.Private?"خاص":p.TrainingFormat==TrainingFormat.SemiPrivate?"شبه خاص":"جماعي")),
            _=>await db.TrainingGroups.Where(g=>keys.Contains(g.Id)).ToDictionaryAsync(g=>g.Id,g=>g.ArabicName)};
        var groupBranches=dimension=="groups"?await db.TrainingGroups.Where(g=>keys.Contains(g.Id)).Select(g=>new{g.Id,branch=g.Branch.ArabicName}).ToDictionaryAsync(g=>g.Id,g=>g.branch):new Dictionary<Guid,string>();
        return counts.Select(c=>new Slice(c.Key?.ToString()??"none",c.Key.HasValue?names.GetValueOrDefault(c.Key.Value,"سجل غير متاح"):"دون مجموعة مسندة",c.Total,dimension=="groups"&&c.Key.HasValue?groupBranches.GetValueOrDefault(c.Key.Value):dimension=="packages"?"الباقة المرجعية للفترة الفعالة الأحدث، أو آخر فترة مسجلة عند عدم وجود فعالة":null)).OrderByDescending(c=>c.Total).ThenBy(c=>c.Key).ToArray();
    }
    private static async Task<IResult> Breakdown(string? dimension,string? state,DateOnly? asOf,DateOnly? from,DateOnly? to,Guid? branchId,Guid? sportId,CurrentTenant tenant,FoundationDbContext db,TimeProvider time,HttpResponse response)
    {
        var t=(await tenant.ResolveAsync())!;if(!await ValidScope(db,t,branchId,sportId))return Results.NotFound();if(!ValidState(state)||!ValidDimension(dimension))return Results.UnprocessableEntity(new{message="اختر حالة وبعدًا صحيحين للاشتراكات."});var date=to??asOf??await AttendanceCalendar.TodayAsync(t.AcademyId,db,time);if(date.Year is <2000 or >2100||from.HasValue&&(from>date||from.Value.Year<2000))return Results.UnprocessableEntity(new{message="اختر تاريخًا صحيحًا."});response.Headers.CacheControl="private, no-store";
        if(dimension=="months") {if(state is not(null or "" or "all" or "subscribed"))return Results.UnprocessableEntity(new{message="نمو الاشتراكات يعرض التسجيلات التي بدأ اشتراكها، دون تصنيفها بحالة اليوم."});var points=await Trend(db,t.AcademyId,date,branchId,sportId);return Results.Ok(new{dimension,asOf=date,state,items=points.Select(p=>new Slice(p.AsOf.ToString("yyyy-MM-dd"),p.Month.ToString("MMMM yyyy",System.Globalization.CultureInfo.GetCultureInfo("ar-EG")),p.Count,"تسجيلات بدأ اشتراكها حتى هذا التاريخ؛ نقاط زمنية لا تُجمع")),total=points.Last().Count,unit="CumulativeEnrollmentSnapshot",additive=false});}
        var rows=Rows(db,t.AcademyId,date,branchId,sportId,state,from);return Results.Ok(new{dimension,asOf=date,state,items=await Slices(dimension!,rows,db,date),total=await rows.CountAsync(),unit="SportEnrollmentsWithSubscriptions"});
    }
    private static async Task<IQueryable<Snapshot>?> Filter(IQueryable<Snapshot> rows,string? dimension,string? key,FoundationDbContext db,DateOnly date)
    {
        if(dimension=="months")return DateOnly.TryParse(key,out _)?rows:null;
        if(string.IsNullOrEmpty(dimension))return string.IsNullOrEmpty(key)?rows:null;if(!ValidDimension(dimension)||string.IsNullOrEmpty(key))return null;
        if(dimension=="days"){
            if(key!="none"&&(!System.Text.RegularExpressions.Regex.IsMatch(key,"^[0-6](,[0-6])*$")||string.Join(",",key.Split(',').Distinct().Order())!=key))return null;
            var groups=await rows.Select(r=>r.GroupId).Distinct().ToArrayAsync();var patterns=await DayPatterns(db,groups.Where(g=>g.HasValue).Select(g=>g!.Value).ToArray(),date);var ids=groups.Where(g=>(g.HasValue?patterns.GetValueOrDefault(g.Value,"none"):"none")==key).ToArray();return rows.Where(r=>ids.Contains(r.GroupId));
        }
        if(key=="none"&&dimension is "groups" or "ages")return rows.Where(r=>r.GroupId==null);
        if(!Guid.TryParse(key,out var id))return null;
        return dimension switch{"branches"=>rows.Where(r=>r.BranchId==id),"sports"=>rows.Where(r=>r.SportId==id),"packages"=>rows.Where(r=>r.PlanId==id),"groups"=>rows.Where(r=>r.GroupId==id),"ages"=>rows.Where(r=>db.TrainingGroups.Any(g=>g.Id==r.GroupId&&g.AgeCategoryId==id)),_=>null};
    }
    private static async Task<IResult> Items(string? state,string? dimension,string? key,DateOnly? asOf,DateOnly? from,DateOnly? to,bool? uniquePlayers,Guid? branchId,Guid? sportId,int? page,CurrentTenant tenant,FoundationDbContext db,TimeProvider time,HttpResponse response,IAuthorizationService authorization,HttpContext context)
    {
        var t=(await tenant.ResolveAsync())!;if(!await ValidScope(db,t,branchId,sportId))return Results.NotFound();if(!ValidState(state)||page is <1 or >100000)return Results.UnprocessableEntity(new{message="اختر حالة وصفحة صحيحين."});var date=to??asOf??await AttendanceCalendar.TodayAsync(t.AcademyId,db,time);if(dimension=="months"){if(!DateOnly.TryParse(key,out var chosen)||chosen>date||chosen.Year<2000)return Results.UnprocessableEntity(new{message="اختر نقطة زمنية صحيحة من الرسم."});date=chosen;}if(date.Year is <2000 or >2100||from.HasValue&&(from>date||from.Value.Year<2000))return Results.UnprocessableEntity(new{message="اختر تاريخًا صحيحًا."});var scoped=await Filter(Rows(db,t.AcademyId,date,branchId,sportId,state,from),dimension,key,db,date);if(scoped is null)return Results.UnprocessableEntity(new{message="اختر تفصيلًا صحيحًا داخل النطاق."});response.Headers.CacheControl="private, no-store";
        var periods=Periods(db,t.AcademyId,date,branchId,sportId,from);var matching=Select(periods,date,state);
        var ids=string.IsNullOrEmpty(dimension)||dimension=="months"?matching.Select(p=>p.SportEnrollmentId):scoped.Select(r=>r.Id);var active=Active(periods,date);var records=db.SportEnrollments.Where(e=>ids.Contains(e.Id));
        var unique=uniquePlayers==true;
        var playerIds=records.Select(e=>e.PlayerId).Distinct();
        var players=db.Players.Where(p=>playerIds.Contains(p.Id)).OrderBy(p=>p.ArabicName).ThenBy(p=>p.Id).Skip(((page??1)-1)*50).Take(50).Select(p=>p.Id);
        var chosenPlayers=unique?await players.ToArrayAsync():Array.Empty<Guid>();
        var pageRecords=unique?records.Where(e=>chosenPlayers.Contains(e.PlayerId)):records.OrderBy(e=>e.Player.ArabicName).ThenBy(e=>e.Id).Skip(((page??1)-1)*50).Take(50);
        // Resolve the bounded page once. Repeating the full effective-period
        // selection for every displayed field created an expensive nested plan.
        var pageIds=await pageRecords.Select(e=>e.Id).ToArrayAsync();
        var pageTerms=await matching.Where(p=>pageIds.Contains(p.SportEnrollmentId)).Select(p=>new{p.Id,p.SportEnrollmentId,p.StartDate,p.Status,plan=p.PlanNameSnapshot??p.SubscriptionPlan.ArabicName,price=p.PriceSnapshot,currency=p.CurrencySnapshot,remainingSessions=p.RemainingSessions,kind=p.PlanTypeSnapshot??p.SubscriptionPlan.PlanType,endDate=p.EndDate}).ToArrayAsync();
        var terms=pageTerms.GroupBy(p=>p.SportEnrollmentId).ToDictionary(g=>g.Key,g=>g.OrderByDescending(p=>p.Status!=SubscriptionPeriodStatus.Frozen&&(p.endDate==null||p.endDate>=date)&&(p.kind==SubscriptionPlanType.Duration||p.remainingSessions>0)).ThenByDescending(p=>p.Status==SubscriptionPeriodStatus.Frozen).ThenByDescending(p=>p.StartDate).ThenByDescending(p=>p.Id).First());
        var activeIds=(await active.Where(p=>pageIds.Contains(p.SportEnrollmentId)).Select(p=>p.SportEnrollmentId).Distinct().ToArrayAsync()).ToHashSet();
        var people=await db.SportEnrollments.Where(e=>pageIds.Contains(e.Id)).OrderBy(e=>e.Player.ArabicName).ThenBy(e=>e.Id).Select(e=>new{e.Id,e.PlayerId,name=e.Player.ArabicName,e.Player.PhotoReference,e.Player.Gender,e.Player.DateOfBirth,e.Player.PlayerCode,e.SportId,criteriaReady=db.EvaluationCriteria.Any(c=>c.AcademyId==t.AcademyId&&c.SportId==e.SportId&&c.IsActive),sport=e.Sport.ArabicName,branch=e.Branch.ArabicName,group=db.TrainingGroups.Where(g=>g.Id==FoundationDbContext.AssignedGroupAt(e.AcademyId,e.Id,date)).Select(g=>g.ArabicName).FirstOrDefault()}).ToArrayAsync();
        var raw=people.Select(e=>{var term=terms[e.Id];return new{e.Id,e.PlayerId,e.name,e.PhotoReference,e.Gender,e.PlayerCode,age=date.Year-e.DateOfBirth.Year-((date.Month<e.DateOfBirth.Month||date.Month==e.DateOfBirth.Month&&date.Day<e.DateOfBirth.Day)?1:0),e.SportId,e.criteriaReady,frozen=term.Status==SubscriptionPeriodStatus.Frozen,term.plan,term.price,term.currency,term.remainingSessions,planType=term.kind.ToString(),e.sport,e.branch,e.group,active=activeIds.Contains(e.Id),term.endDate};}).ToArray();

        object items=unique?raw.GroupBy(r=>r.PlayerId).Select(g=>new{id=g.Key,playerId=g.Key,name=g.First().name,photoReference=g.First().PhotoReference,gender=g.First().Gender,playerCode=g.First().PlayerCode,age=g.First().age,sports=g.ToArray()}).ToArray():raw;
        return Results.Ok(new{items,canEvaluate=(await authorization.AuthorizeAsync(context.User,null,AcademyPermissions.EvaluationManage)).Succeeded,canOpenPlayer=(await authorization.AuthorizeAsync(context.User,null,AcademyPermissions.PeopleManage)).Succeeded,total=unique?await playerIds.CountAsync():await records.CountAsync(),page=page??1,asOf=date,from,to=date,state,dimension,key,unit=unique?"DistinctPlayers":"SportEnrollmentsWithSubscriptions"});

    }
}

public sealed record ExpiryReminderWrite(Guid? BranchId=null,Guid? SportId=null);
