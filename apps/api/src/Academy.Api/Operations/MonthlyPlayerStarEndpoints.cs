using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Api.Slice5;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Operations;
public static class MonthlyPlayerStarEndpoints
{
    public static void MapMonthlyPlayerStars(this WebApplication app)
    {
        var api=app.MapGroup("/api/v1");
        api.MapGet("/coach/monthly-stars",Options).RequireAuthorization(AcademyPermissions.TenantAccess);
        api.MapPost("/coach/monthly-stars",Grant).RequireAuthorization(AcademyPermissions.EvaluationManage).AddEndpointFilter<CsrfFilter>();
        api.MapGet("/reports/featured-players",Featured).RequireAuthorization(AcademyPermissions.OwnerDashboardRead);
    }
    private static IQueryable<MatchTeam> Teams(FoundationDbContext db,Guid academy,Guid coach)=>db.MatchTeams.Where(t=>t.AcademyId==academy&&t.IsActive&&t.CoachMembershipId==coach);
    private static IQueryable<Guid> Eligible(FoundationDbContext db,Guid academy,Guid team)=>db.MatchParticipants.Where(p=>p.AcademyId==academy&&p.IsActive
        &&db.AcademyMatches.Any(m=>m.AcademyId==academy&&m.Id==p.MatchId&&m.TeamId==team&&m.IsActive&&m.PublishedAtUtc!=null)
        &&db.MatchTeamPlayers.Any(r=>r.AcademyId==academy&&r.TeamId==team&&r.IsActive&&db.SportEnrollments.Any(e=>e.AcademyId==academy&&e.Id==r.EnrollmentId&&e.IsActive&&e.PlayerId==p.PlayerId&&e.Player.IsActive)))
        .Select(p=>p.PlayerId).Distinct();
    private static async Task<IResult> Options(CurrentTenant tenant,FoundationDbContext db,TimeProvider clock,HttpResponse response)
    {
        var t=(await tenant.ResolveAsync())!;if(t.Role!=AcademyRole.Coach)return Results.Forbid();response.Headers.CacheControl="private, no-store";
        var today=await AttendanceCalendar.TodayAsync(t.AcademyId,db,clock);var month=new DateOnly(today.Year,today.Month,1);
        var teams=await Teams(db,t.AcademyId,t.MembershipId).OrderBy(g=>g.Name).ToArrayAsync();
        var items=new List<object>();foreach(var team in teams){var ids=Eligible(db,t.AcademyId,team.Id);var grants=await db.MonthlyPlayerStars.Where(s=>s.AcademyId==t.AcademyId&&s.TeamId==team.Id&&s.CoachMembershipId==t.MembershipId&&s.Month==month).ToArrayAsync();
            var players=await db.Players.Where(p=>p.AcademyId==t.AcademyId&&ids.Contains(p.Id)).OrderBy(p=>p.ArabicName).Select(p=>new{p.Id,p.ArabicName,p.PhotoReference}).ToArrayAsync();
            items.Add(new{team.Id,team.Name,remaining=3-grants.Length,players=players.Select(p=>new{p.Id,p.ArabicName,p.PhotoReference,stars=grants.Count(g=>g.PlayerId==p.Id)})});}
        return Results.Ok(new{month,teams=items});
    }
    public sealed record GrantWrite(Guid TeamId,Guid PlayerId,Guid RequestId);
    private static async Task<IResult> Grant(GrantWrite body,CurrentTenant tenant,FoundationDbContext db,TimeProvider clock)
    {
        var t=(await tenant.ResolveAsync())!;if(t.Role!=AcademyRole.Coach)return Results.Forbid();
        if(body.RequestId==Guid.Empty)return Results.UnprocessableEntity(new{message="أعد فتح طلب منح النجمة."});
        var today=await AttendanceCalendar.TodayAsync(t.AcademyId,db,clock);var month=new DateOnly(today.Year,today.Month,1);
        await using var tx=await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"monthly-star-request:{t.AcademyId}:{t.MembershipId}:{body.RequestId}"},0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"monthly-star:{t.AcademyId}:{t.MembershipId}:{body.TeamId}:{month}"},0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"MatchTeams\" WHERE \"AcademyId\"={t.AcademyId} AND \"Id\"={body.TeamId} FOR UPDATE");
        var prior=await db.MonthlyPlayerStars.SingleOrDefaultAsync(s=>s.AcademyId==t.AcademyId&&s.CoachMembershipId==t.MembershipId&&s.RequestId==body.RequestId);
        if(prior!=null)return prior.TeamId==body.TeamId&&prior.PlayerId==body.PlayerId?Results.Ok(new{prior.Id,replay=true}):Results.Conflict(new{message="مرجع النجمة مستخدم لطلب مختلف."});
        // Recheck trusted membership under the same grant lock; no client boolean is an authorization.
        var member=await db.AcademyMemberships.SingleOrDefaultAsync(m=>m.AcademyId==t.AcademyId&&m.Id==t.MembershipId&&m.IsActive&&m.Role==AcademyRole.Coach&&m.User.IsActive);
        if(member==null||!await Teams(db,t.AcademyId,t.MembershipId).AnyAsync(g=>g.Id==body.TeamId)||!await Eligible(db,t.AcademyId,body.TeamId).ContainsAsync(body.PlayerId))return Results.NotFound();
        var count=await db.MonthlyPlayerStars.CountAsync(s=>s.AcademyId==t.AcademyId&&s.CoachMembershipId==t.MembershipId&&s.TeamId==body.TeamId&&s.Month==month);
        if(count>=3)return Results.Conflict(new{message="استخدمت نجوم هذا الفريق الثلاث لهذا الشهر."});
        var row=new MonthlyPlayerStar{Id=Guid.NewGuid(),AcademyId=t.AcademyId,CoachMembershipId=t.MembershipId,TeamId=body.TeamId,PlayerId=body.PlayerId,Month=month,Slot=count+1,RequestId=body.RequestId,CreatedAtUtc=clock.GetUtcNow(),UpdatedAtUtc=clock.GetUtcNow()};db.MonthlyPlayerStars.Add(row);
        var name=await db.Players.Where(p=>p.Id==body.PlayerId).Select(p=>p.ArabicName).SingleAsync();
        var template=await db.NoticeTemplates.Where(n=>n.AcademyId==t.AcademyId&&n.Key=="Star").Select(n=>n.Message).SingleOrDefaultAsync()??NoticeEndpoints.Defaults["Star"];
        var families=await db.GuardianPlayerLinks.Where(l=>l.AcademyId==t.AcademyId&&l.PlayerId==body.PlayerId&&l.IsActive&&l.Guardian.IsActive&&l.Guardian.User.IsActive).Select(l=>new{l.Guardian.UserId,l.Guardian.DisplayName}).Distinct().ToArrayAsync();
        foreach(var f in families)db.AcademyNotices.Add(new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,UserId=f.UserId,AudienceRole="Guardian",TargetKind="Stars",TargetId=body.PlayerId,Message=template.Replace("{children}",name).Replace("{guardian}",f.DisplayName),CreatedAtUtc=clock.GetUtcNow(),UpdatedAtUtc=clock.GetUtcNow()});
        await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(new{row.Id,replay=false,remaining=2-count});
    }
    private static async Task<IResult> Featured(DateOnly? asOf,Guid? branchId,int? page,CurrentTenant tenant,FoundationDbContext db,TimeProvider clock,IEvaluationReportCalculator calculator,HttpResponse response)
    {
        var t=(await tenant.ResolveAsync())!;if(t.Role!=AcademyRole.AcademyOwner)return Results.Forbid();response.Headers.CacheControl="private, no-store";
        if(page is <1 or >100000)return Results.UnprocessableEntity(new{message="رقم الصفحة غير صالح."});
        var date=asOf??await AttendanceCalendar.TodayAsync(t.AcademyId,db,clock);var month=new DateOnly(date.Year,date.Month,1);
        var evals=await db.PlayerEvaluations.AsNoTracking().Include(e=>e.Scores).Include(e=>e.SportEnrollment).ThenInclude(e=>e.Player).Include(e=>e.TrainingGroup).ThenInclude(g=>g.Branch)
            .Where(e=>e.AcademyId==t.AcademyId&&e.Status==EvaluationStatus.Published&&e.IsActive&&e.Sport.Kind==SportKind.Football&&e.EvaluationDate<=date&&e.SportEnrollment.IsActive&&e.SportEnrollment.Player.IsActive&&(!branchId.HasValue||e.TrainingGroup.BranchId==branchId))
            .ToArrayAsync();
        var stars=await db.MonthlyPlayerStars.Where(s=>s.AcademyId==t.AcademyId&&s.Month==month&&(!branchId.HasValue||db.MatchTeams.Any(team=>team.Id==s.TeamId&&team.BranchId==branchId))).GroupBy(s=>s.PlayerId).Select(g=>new{player=g.Key,count=g.Count()}).ToDictionaryAsync(g=>g.player,g=>g.count);
        var comparable=evals.OrderByDescending(e=>e.EvaluationDate).ThenByDescending(e=>e.PublishedAtUtc).ThenBy(e=>e.Id).GroupBy(e=>e.SportEnrollment.PlayerId).Select(g=>g.First())
            .Select(e=>new{evaluation=e,score=calculator.Calculate(e.Scores)}).Where(r=>r.score.OverallScore.HasValue&&r.score.AvailableAxes==6&&r.score.CompletenessPercentage==100)
            .Select(r=>new{id=r.evaluation.SportEnrollment.PlayerId,name=r.evaluation.SportEnrollment.Player.ArabicName,photoReference=r.evaluation.SportEnrollment.Player.PhotoReference,branch=r.evaluation.TrainingGroup.Branch.ArabicName,
                score=r.score.OverallScore!.Value,stars=stars.GetValueOrDefault(r.evaluation.SportEnrollment.PlayerId),index=MonthlyStarRanking.Index(r.score.OverallScore!.Value,stars.GetValueOrDefault(r.evaluation.SportEnrollment.PlayerId)),evaluationId=r.evaluation.Id})
            .OrderByDescending(r=>r.index).ThenByDescending(r=>r.score).ThenBy(r=>r.name).ThenBy(r=>r.id).ToArray();
        return Results.Ok(new{month,asOf=date,total=comparable.Length,page=page??1,items=comparable.Skip(((page??1)-1)*25).Take(25),basis="تقييم كرة قدم منشور مكتمل بالمحاور الستة، ونجوم شهر نهاية الفترة فقط. الرقم الظاهر هو التقييم؛ مؤشر الترتيب منفصل."});
    }
}
