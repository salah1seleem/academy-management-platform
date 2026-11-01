using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice8;
public static partial class ReportingEndpoints
{
    // Same enrollment grain and predicates as OwnerSummary. A child with two
    // sports can appear twice: these are subscriptions, never a player total.
    private static async Task<IResult> SubscriptionSnapshot(string? state,DateOnly? asOf,Guid? branchId,int? page,CurrentTenant tenant,FoundationDbContext db,TimeProvider time)
    {
        var t=(await tenant.ResolveAsync())!;if(t.Role!=AcademyRole.AcademyOwner)return Results.Forbid();
        if(page is <1 or >100000||state is not(null or "" or "all" or "subscribed" or "active" or "expiring" or "expired"))return Results.UnprocessableEntity(new{message="اختر حالة اشتراك وصفحة صحيحة."});
        var date=asOf??await AttendanceCalendar.TodayAsync(t.AcademyId,db,time);
        var total=SubscriptionHomeEndpoints.Periods(db,t.AcademyId,date,branchId);
        var active=SubscriptionHomeEndpoints.Active(total,date);
        var selected=SubscriptionHomeEndpoints.Select(total,date,state);
        var ids=selected.Select(p=>p.SportEnrollmentId).Distinct();
        var q=db.SportEnrollments.Where(e=>e.AcademyId==t.AcademyId&&ids.Contains(e.Id));
        var items=await q.OrderBy(e=>e.Player.ArabicName).ThenBy(e=>e.Id).Skip(((page??1)-1)*50).Take(50).Select(e=>new{e.Id,e.PlayerId,name=e.Player.ArabicName,e.Player.PhotoReference,sport=e.Sport.ArabicName,branch=e.Branch.ArabicName,group=e.TrainingGroup==null?null:e.TrainingGroup.ArabicName,
            active=active.Any(p=>p.SportEnrollmentId==e.Id),endDate=selected.Where(p=>p.SportEnrollmentId==e.Id).OrderByDescending(p=>p.StartDate).ThenByDescending(p=>p.Id).Select(p=>p.EndDate).FirstOrDefault()}).ToArrayAsync();
        return Results.Ok(new{items,total=await q.CountAsync(),page=page??1,asOf=date,state});
    }
}
