using System.Globalization;
using System.Text;
using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice5;

// A live, private reading of existing published evidence, not a new assessment or AI report.
public static class PlayerMonthlyEvidence
{
    public sealed record Entry(Guid Id,string Kind,DateOnly On,string Title,string State,decimal? Value,string? Unit,string? Note,string Actor,Guid? PlanId=null);
    public static void MapMonthlyEvidence(this WebApplication app)
    {
        app.MapGet("/api/v1/evaluations/monthly/enrollments/{enrollmentId:guid}",Read)
            .RequireAuthorization(AcademyPermissions.EvaluationRead);
        app.MapGet("/api/v1/guardian/children/{playerId:guid}/monthly",Read)
            .RequireAuthorization(AcademyPermissions.GuardianEvaluationRead);
    }
    public static DateOnly? Month(string? month,DateOnly today)
    {
        if(month==null)return new(today.Year,today.Month,1);
        if(!DateOnly.TryParseExact(month+"-01","yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day)||day.Year<1900||day>today)return null;
        return day;
    }
    public static string Cell(string? value)
    {
        var s=value??"";var leading=s.TrimStart();
        if(leading.Length>0&&leading[0] is '=' or '+' or '-' or '@'||s.StartsWith('\t')||s.StartsWith('\r'))s="'"+s;
        return "\""+s.Replace("\"","\"\"")+"\"";
    }
    public static string Csv(IEnumerable<string[]> rows)=>string.Join("\r\n",rows.Select(r=>string.Join(',',r.Select(Cell))))+"\r\n";
    public static bool ExportFits(int skills,int badges,int certificates)=>skills is >=0 and <=2000&&badges is >=0 and <=2000&&certificates is >=0 and <=2000;
    private static async Task<IResult> Read(Guid? playerId,Guid? enrollmentId,string? month,int? page,string? format,
        CurrentTenant tenant,FoundationDbContext db,HttpContext h,TimeProvider clock)
    {
        h.Response.Headers.CacheControl="private, no-store";
        h.Response.Headers["X-Content-Type-Options"]="nosniff";
        var t=(await tenant.ResolveAsync())!;var family=playerId.HasValue;
        if(family&&!await db.GuardianPlayerLinks.AnyAsync(l=>l.PlayerId==playerId&&l.IsActive&&l.Player.IsActive&&l.Guardian.IsActive&&l.Guardian.UserId==db.RequestUserId))return Results.NotFound();
        var today=await AttendanceCalendar.TodayAsync(t.AcademyId,db,clock);var start=Month(month,today);
        if(start==null||page is <0 or >1000||format is not(null or "csv" or "text"))return Results.BadRequest(new{message="اختر شهرًا صحيحًا حتى الشهر الحالي، وصفحة صحيحة للتقرير."});
        var end=start.Value.AddMonths(1).AddDays(-1);if(end>today)end=today;
        await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead);
        var options=family?await db.SportEnrollments.AsNoTracking().Where(e=>e.PlayerId==playerId&&e.IsActive&&e.Player.IsActive&&e.Sport.IsActive&&e.Branch.IsActive).OrderBy(e=>e.Id).Select(e=>new{e.Id,sport=e.Sport.ArabicName,branch=e.Branch.ArabicName}).Take(101).ToArrayAsync():[];
        if(family&&options.Length==0)return Results.NotFound();
        var id=enrollmentId??options.FirstOrDefault()?.Id;
        if(id==null)return Results.NotFound();
        var context=await PlayerGoalEndpoints.Enrollment(id.Value,t,db,clock);
        if(context==null||family&&context.PlayerId!=playerId)return Results.NotFound();
        var reviews=from r in db.PlayerSkillReviews.AsNoTracking()
                    join p in db.PlayerSkillPlans on r.PlanId equals p.Id
                    join c in db.SkillCurricula on r.CurriculumId equals c.Id
                    join q in db.SkillRequirements on r.RequirementId equals q.Id
                    where p.SportEnrollmentId==id&&c.State=="Published"&&r.ObservedOn>=start&&r.ObservedOn<=end
                    select new {r.Id,On=r.ObservedOn,Title=q.Name,r.State,Value=r.MeasuredValue,q.Unit,r.Note,Actor=r.ActorName,PlanId=p.Id};
        var badges=from g in db.PlayerBadgeGrants.AsNoTracking()
                   join r in db.PlayerBadgeRules on g.RuleId equals r.Id
                   where g.SportEnrollmentId==id&&g.ObservedOn>=start&&g.ObservedOn<=end
                   select new {g.Id,On=g.ObservedOn,r.Title,State=db.PlayerBadgeRevocations.Any(v=>v.GrantId==g.Id)?"Revoked":"Granted",Value=r.Points,Note=g.FamilyNote,Actor=g.ActorName};
        var certificates=db.PlayerAchievementCertificates.AsNoTracking().Where(c=>c.SportEnrollmentId==id&&c.IssuedOn>=start&&c.IssuedOn<=end)
            .Select(c=>new {c.Id,On=c.IssuedOn,Title=c.LevelName,State=db.PlayerCertificateWithdrawals.Any(w=>w.CertificateId==c.Id)?"Withdrawn":"Issued",Actor=c.ActorName});
        var counts=new{skills=await reviews.CountAsync(),badges=await badges.CountAsync(),certificates=await certificates.CountAsync()};
        var exporting=format!=null;const int size=25,exportLimit=2000;var offset=exporting?0:(page??0)*size;var take=exporting?exportLimit:size;
        if(exporting&&!ExportFits(counts.skills,counts.badges,counts.certificates))return Results.Conflict(new{message="هذا الشهر يتجاوز حد التصدير الآمن. راجع المصادر؛ لم نصدر ملفًا ناقصًا."});
        var rows=new List<Entry>();
        rows.AddRange((await reviews.OrderByDescending(r=>r.On).ThenBy(r=>r.Id).Skip(offset).Take(take).ToArrayAsync()).Select(r=>new Entry(r.Id,"Skill",r.On,r.Title,r.State,r.Value,r.Unit,r.Note,r.Actor,r.PlanId)));
        rows.AddRange((await badges.OrderByDescending(r=>r.On).ThenBy(r=>r.Id).Skip(offset).Take(take).ToArrayAsync()).Select(r=>new Entry(r.Id,"Badge",r.On,r.Title,r.State,r.Value,"Encouragement",r.Note,r.Actor)));
        rows.AddRange((await certificates.OrderByDescending(r=>r.On).ThenBy(r=>r.Id).Skip(offset).Take(take).ToArrayAsync()).Select(r=>new Entry(r.Id,"Certificate",r.On,r.Title,r.State,null,null,null,r.Actor)));
        var ordered=rows.OrderByDescending(r=>r.On).ThenBy(r=>r.Kind).ThenBy(r=>r.Id).ToArray();
        if(exporting)
        {
            var header=new[]{"اللاعب","الرياضة","الشهر","النوع","التاريخ","الدليل","الحالة","القيمة","الوحدة","الملاحظة المنشورة","المراجع","مرجع المصدر"};
            var csv=Csv(new[]{header}.Concat(ordered.Select(r=>new[]{context.Player,context.Sport,start.Value.ToString("yyyy-MM"),Kind(r.Kind),r.On.ToString("yyyy-MM-dd"),r.Title,State(r.State),r.Value?.ToString(CultureInfo.InvariantCulture)??"",Unit(r.Unit),r.Note??"",r.Actor,r.Id.ToString()})));
            if(format=="text")return Results.Ok(new{text=csv});
            return Results.File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(),"text/csv; charset=utf-8",$"player-development-{start:yyyy-MM}.csv");
        }
        return Results.Ok(new{enrollment=context,enrollments=options.Take(100),enrollmentsHasMore=options.Length>100,month=start.Value.ToString("yyyy-MM"),from=start,to=end,asOfDate=today,generatedAtUtc=clock.GetUtcNow(),page=page??0,pageSizePerKind=size,counts,items=ordered,hasMore=counts.skills>offset+size||counts.badges>offset+size||counts.certificates>offset+size,
            notice="أدلة فنية منشورة فقط، لا درجة قوة أو توقع احتراف. التاريخ هو الملاحظة للمهارات والشارات، والإصدار للشهادة. السحب يظهر بحالته الحالية. كل رياضة مستقلة؛ غياب الأدلة لا يعني صفر مهارة. لا حضور أو صور أو بيانات طبية أو مالية في هذا التقرير، ولا يشترط تجديد الاشتراك."});
    }
    public static string Kind(string v)=>v switch{"Skill"=>"مراجعة مهارة","Badge"=>"شارة مجهود","Certificate"=>"شهادة إنجاز",_=>"دليل"};
    public static string State(string v)=>v switch{"Mastered"=>"أتقن","Learning"=>"قيد التعلم","NotAssessed"=>"لم يقيّم","Revoked"=>"شارة مسحوبة","Granted"=>"شارة ممنوحة","Withdrawn"=>"شهادة مسحوبة","Issued"=>"شهادة صادرة",_=>"راجع المصدر"};
    public static string Unit(string? v)=>v switch{null=>"","Checklist"=>"قائمة تحقق","Seconds"=>"ثانية","Metres"=>"متر","Count"=>"عدد","Encouragement"=>"نقاط تشجيعية غير مالية",_=>"قياس — راجع المصدر"};
}
