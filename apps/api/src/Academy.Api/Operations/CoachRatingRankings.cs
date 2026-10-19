using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static partial class GuardianFeedbackEndpoints
{
    private static async Task<IResult> CoachRankings(DateOnly? from, DateOnly? to, bool? allDates, Guid? branchId, Guid? sportId,
        CurrentTenant tenant, FoundationDbContext db, TimeProvider clock, HttpContext http)
    {
        var t=(await tenant.ResolveAsync())!;if(t.Role!=AcademyRole.AcademyOwner)return Results.Forbid();http.Response.Headers.CacheControl="private, no-store";
        var today=await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(t.AcademyId,db,clock);
        var start=allDates==true?from:from??new DateOnly(today.Year,today.Month,1);var end=allDates==true?to:to??today;
        if(start>end||end==DateOnly.MaxValue)return Results.UnprocessableEntity(new{message="اختر فترة تقييم صحيحة."});
        var zone=TimeZoneInfo.FindSystemTimeZoneById(await db.Academies.Where(a=>a.Id==t.AcademyId).Select(a=>a.TimeZone).SingleAsync());
        DateTimeOffset? first=start.HasValue?TimeZoneInfo.ConvertTimeToUtc(start.Value.ToDateTime(TimeOnly.MinValue),zone):null;
        DateTimeOffset? after=end.HasValue?TimeZoneInfo.ConvertTimeToUtc(end.Value.AddDays(1).ToDateTime(TimeOnly.MinValue),zone):null;
        var ratings=db.GuardianFeedbacks.AsNoTracking().Where(f=>f.AcademyId==t.AcademyId&&f.IsActive&&f.Kind=="Rating"&&f.CoachMembershipId!=null&&f.Score>=1&&f.Score<=5
            &&(!first.HasValue||f.CreatedAtUtc>=first)&&(!after.HasValue||f.CreatedAtUtc<after)&&(!branchId.HasValue||f.BranchId==branchId)
            &&(!sportId.HasValue||db.TrainingGroups.Any(g=>g.AcademyId==t.AcademyId&&g.Id==f.TrainingGroupId&&g.SportId==sportId)));
        var aggregate=ratings.GroupBy(f=>f.CoachMembershipId!.Value).Select(g=>new{Id=g.Key,ReviewCount=g.Count(),Average=g.Average(x=>(decimal)x.Score!.Value)});
        var coaches=from r in aggregate join c in db.AcademyMemberships.AsNoTracking().Where(c=>c.AcademyId==t.AcademyId&&c.IsActive&&c.User.IsActive&&c.Role==AcademyRole.Coach) on r.Id equals c.Id
            select new{r.Id,name=c.User.DisplayName,r.ReviewCount,averageRating=Math.Round(r.Average,2)};
        const int minimumReviews=3;
        var eligible=coaches.Where(c=>c.ReviewCount>=minimumReviews);
        var highest=await eligible.OrderByDescending(c=>c.averageRating).ThenByDescending(c=>c.ReviewCount).ThenBy(c=>c.name).ThenBy(c=>c.Id).Take(5).ToArrayAsync();
        var lowest=await eligible.OrderBy(c=>c.averageRating).ThenByDescending(c=>c.ReviewCount).ThenBy(c=>c.name).ThenBy(c=>c.Id).Take(5).ToArrayAsync();
        return Results.Ok(new{from=start,to=end,minimumReviews,highest,lowest,insufficientSampleCount=await coaches.CountAsync(c=>c.ReviewCount<minimumReviews),basis="تقييمات أولياء الأمور المقدمة؛ ليست تقييمات فنية للاعبين أو حكمًا آليًا على المدرب."});
    }
}
