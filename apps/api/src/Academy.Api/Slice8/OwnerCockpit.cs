using Academy.Api.Auth;
using Academy.Api.Configuration;
using Academy.Api.Finance;
using Academy.Api.Operations;
using Academy.Api.Slice3;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice8;
public static partial class ReportingEndpoints
{
    private static async Task<IResult> OwnerCockpit(DateOnly? from,DateOnly? to,bool? allDates,Guid? branchId,CurrentTenant tenant,FoundationDbContext db,
        ISubscriptionClock clock,TimeProvider actualClock,AcademyConfigurationService configuration,DiscountRules pricing,HttpContext http)
    {
        var t=(await tenant.ResolveAsync())!;if(t.Role!=AcademyRole.AcademyOwner)return Results.Forbid();http.Response.Headers.CacheControl="private, no-store";
        var today=await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId,db,actualClock);
        var end=to??today;var start=from??end.AddMonths(-1);
        var all=allDates==true&&from==null&&to==null;if(!all&&start.Year<2000)return Validation("from","اختر فترة من سنة2000 أو بعدها.");
        var range=await FinancialRange(t.AcademyId,all?null:start,all?null:end,db,clock,all);if(range.Error!=null)return range.Error;
        if(branchId.HasValue&&!await db.Branches.AnyAsync(b=>b.Id==branchId&&b.AcademyId==t.AcademyId))return Results.NotFound();
        var zone=await db.Academies.Where(a=>a.Id==t.AcademyId).Select(a=>a.TimeZone).SingleAsync();
        // Equal-length adjacent window: no overlap and no misleading percentage
        // when calendar months have different lengths.
        var previousTo=start.AddDays(-1);var previousFrom=previousTo.AddDays(-(end.DayNumber-start.DayNumber));
        var previousBounds=UtcRange(previousFrom,previousTo,zone);var previous=range with{From=previousFrom,To=previousTo,StartUtc=previousBounds.Start,EndUtcExclusive=previousBounds.EndExclusive};
        var collected=await CockpitNetCollections(t.AcademyId,range,branchId,db);var before=all?0:await CockpitNetCollections(t.AcademyId,previous,branchId,db);
        var comparable=!all;
        decimal? change=before>0&&comparable?Math.Round((collected-before)*100/before,1):null;
        var settings=await configuration.Active(t.AcademyId);var target=!branchId.HasValue&&!all&&start.Day==1&&start.Year==today.Year&&start.Month==today.Month&&end==today?settings?.MonthlyCollectionTarget:null;
        var balance=ReceivableEndpoints.Rows(db,t.AcademyId,clock.UtcNow,clock.Today,branchId,null,null).Where(b=>b.Currency==range.Currency&&b.Outstanding>0&&b.Overdue);
        var overdue=await balance.SumAsync(b=>(decimal?)b.Outstanding)??0;
        var families=await db.GuardianPlayerLinks.Where(l=>l.AcademyId==t.AcademyId&&l.IsActive&&l.Guardian.IsActive&&l.Player.IsActive&&balance.Any(b=>b.PlayerId==l.PlayerId)).Select(l=>l.GuardianId).Distinct().CountAsync();
        var renewals=await RenewalOpportunityEndpoints.Build(db,pricing,clock,t.AcademyId,UserId(http.User),new(branchId,null,null,null,null));
        var local=TimeZoneInfo.ConvertTime(actualClock.GetUtcNow(),TimeZoneInfo.FindSystemTimeZoneById(zone));var time=TimeOnly.FromDateTime(local.DateTime);
        var attendance=db.PlayerAttendances.Where(a=>a.AcademyId==t.AcademyId&&a.IsActive&&a.TrainingSession.IsActive&&a.TrainingSession.Status!=TrainingSessionStatus.Cancelled&&a.TrainingSession.SessionDate>=range.From&&a.TrainingSession.SessionDate<=range.To&&(!branchId.HasValue||a.TrainingSession.BranchId==branchId)
            &&(a.TrainingSession.SessionDate<today||a.TrainingSession.SessionDate==today&&a.TrainingSession.EndTime<=time));
        var recorded=await attendance.CountAsync(a=>a.Status!=AttendanceStatus.NotRecorded);var attended=await attendance.CountAsync(a=>a.Status==AttendanceStatus.Present||a.Status==AttendanceStatus.Late);
        var signals=(await OwnerFollowUpEndpoints.Signals(db,t.AcademyId,today,asOf:actualClock.GetUtcNow())).Where(g=>g.SourceActive&&(!branchId.HasValue||g.BranchId==branchId)).ToArray();
        var plans=await db.SubscriptionPlans.AsNoTracking().Where(p=>p.AcademyId==t.AcademyId&&p.IsActive&&p.Currency==range.Currency&&p.ServiceKind==AcademyServiceKind.Training&&p.TrainingFormat==TrainingFormat.Group&&p.PlanType==SubscriptionPlanType.Duration&&p.DurationMonths==1).Select(p=>new{p.Id,p.SportId,p.Price,p.AllBranches,branches=p.BranchPublications.Where(b=>b.IsActive).Select(b=>b.BranchId).ToArray()}).ToArrayAsync();
        var capacity=signals.Where(g=>g.Capacity>0).Select(g=>{var available=Math.Max(0,g.Capacity!.Value-g.Enrollments);var plan=plans.Where(p=>p.SportId==g.SportId&&(p.AllBranches||p.branches.Contains(g.BranchId))).OrderBy(p=>p.Price).ThenBy(p=>p.Id).FirstOrDefault();return new{g.GroupId,g.BranchId,g.Name,g.Branch,g.Sport,g.Capacity,occupied=g.Enrollments,available,planId=plan?.Id,baseMonthlyPrice=plan?.Price,conditionalValue=plan==null?(decimal?)null:Math.Round(plan.Price*available,2)};}).ToArray();
        return Results.Ok(new{from=range.From,to=range.To,currency=range.Currency,collections=new{net=collected,previous=before,previousFrom,previousTo,changePercentage=change,comparable,comparisonBasis="الفترة السابقة مباشرة بنفس عدد الأيام ودون تداخل؛ لا نسبة عند صفر سابق."},target=new{amount=target,achievement=target>0?Math.Round(collected*100/target.Value,1):(decimal?)null,basis="الهدف الشهري الساري حاليًا من إعدادات الأكاديمية، لا هدف تاريخي مفترض."},
            overdue=new{amount=overdue,families,asOf=clock.Today},risk=new{players=renewals.Items.Select(r=>r.PlayerId).Distinct().Count(),asOf=renewals.QuoteDay,basis="اشتراك مدفوع سابق انتهى بلا خدمة لاحقة مؤهلة للتجديد؛ ليس تنبؤًا بمغادرة مؤكدة."},
            attendance=new{percentage=recorded>0?Math.Round(attended*100m/recorded,1):(decimal?)null,recorded,attended,notRecorded=await attendance.CountAsync(a=>a.Status==AttendanceStatus.NotRecorded),basis="حاضر ومتأخر من السجلات المكتملة لحصص انتهت؛ غير المسجل والمستقبلي والملغى خارج النسبة."},
            followups=new{overdue=await db.OwnerFollowUps.CountAsync(f=>f.AcademyId==t.AcademyId&&f.State!="Closed"&&f.State!="Rejected"&&f.DueOn<today&&(!branchId.HasValue||f.SourceBranchId==branchId||f.SourceKind=="Group"&&db.TrainingGroups.Any(g=>g.Id==f.TrainingGroupId&&g.BranchId==branchId))),unassigned=await db.OwnerFollowUps.CountAsync(f=>f.AcademyId==t.AcademyId&&f.State!="Closed"&&f.State!="Rejected"&&f.AssignedToMembershipId==null&&(!branchId.HasValue||f.SourceBranchId==branchId))},
            capacity=new{asOf=today,seats=capacity.Sum(g=>g.available),unknownGroups=signals.Count(g=>g.Capacity is null or <=0),conditionalValue=capacity.Sum(g=>g.conditionalValue??0),unpricedGroups=capacity.Count(g=>g.available>0&&g.baseMonthlyPrice==null),items=capacity,basis="السعة ناقص التسجيلات الفعالة المسندة اليوم. قيمة الفرصة مشروطة بأقل باقة جماعي شهرية منشورة للفرع والرياضة، قبل الخصومات؛ ليست ربحًا أو تحصيلًا."}});
    }
    private static async Task<decimal> CockpitNetCollections(Guid academy,FinancialRangeResult r,Guid? branch,FoundationDbContext db)=>
        (await FinancialQuery(academy,r.Currency,r.StartUtc,r.EndUtcExclusive,null,branch,null,null,null,db).SumAsync(c=>(decimal?)c.Amount)??0)
        +(await ProductFinancialQuery(academy,r.Currency,r.StartUtc,r.EndUtcExclusive,null,branch,null,null,db).SumAsync(c=>(decimal?)c.Amount)??0)
        +(await TournamentQuery(academy,r.Currency,r.StartUtc,r.EndUtcExclusive,null,branch,null,null,db).SumAsync(c=>(decimal?)c.Amount)??0)
        +(await DayPassFinancialQuery(academy,r.Currency,r.StartUtc,r.EndUtcExclusive,null,branch,null,null,null,db).SumAsync(c=>(decimal?)c.Amount)??0)
        +(await ExternalFinancialQuery(academy,r.Currency,r.StartUtc,r.EndUtcExclusive,null,branch,null,null,null,db).SumAsync(c=>(decimal?)c.Amount)??0)
        -(await RefundFinancialQuery(academy,r.Currency,r.StartUtc,r.EndUtcExclusive,null,branch,null,null,null,db).SumAsync(c=>(decimal?)c.Amount)??0);
}
