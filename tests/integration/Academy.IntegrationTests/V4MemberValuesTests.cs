using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    // Written with V4-01A; execution is deliberately reserved for the final V4 gates.
    [Fact] public async Task V4_members_use_subscription_snapshots_and_server_sort_pages_without_payer_attribution()
    {
        using var owner=await Staff();using var foreign=await Staff(DemoSeed.FutureOwnerEmail);using var coach=await Mobile(AcademyRole.Coach);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var day=new DateOnly(2026,9,28);
        var periods=await db.SubscriptionPeriods.AsNoTracking().Include(p=>p.SportEnrollment).ThenInclude(e=>e.Player).Include(p=>p.Collection)
            .Where(p=>p.AcademyId==DemoSeed.NogoomAcademyId&&p.IsActive&&(p.Status==SubscriptionPeriodStatus.Active||p.Status==SubscriptionPeriodStatus.Scheduled)&&p.StartDate<=day&&(p.EndDate==null||p.EndDate>=day)&&p.SportEnrollment.IsActive&&p.SportEnrollment.Status==Academy.Infrastructure.People.EnrollmentStatus.Active&&p.SportEnrollment.Player.IsActive
                &&(p.PlanTypeSnapshot==SubscriptionPlanType.Duration||p.PlanTypeSnapshot==null&&p.InitialSessions==null||p.RemainingSessions>0)).ToArrayAsync();
        var effective=periods.GroupBy(p=>p.SportEnrollmentId).Select(g=>g.OrderByDescending(p=>p.StartDate).ThenByDescending(p=>p.CreatedAtUtc).ThenByDescending(p=>p.Id).First()).ToArray();
        decimal? Expected(SubscriptionPeriod p){var price=p.PriceSnapshot??p.Collection?.Amount;var days=p.DurationDaysSnapshot??(p.EndDate.HasValue?p.EndDate.Value.DayNumber-p.StartDate.DayNumber+1:(int?)null);return p.CurrencySnapshot!="EGP"||price==null||price<0?null:price==0?0m:p.DurationMonthsSnapshot>0?price/p.DurationMonthsSnapshot:days>0?price*30m/days:null;}
        var all=await owner.GetFromJsonAsync<JsonElement>("/api/v1/people/players?asOf=2026-09-28&sort=value_desc");Assert.Equal(JsonValueKind.Array,all.ValueKind);
        foreach(var item in all.EnumerateArray()){
            var own=effective.Where(p=>p.SportEnrollment.PlayerId==item.GetProperty("id").GetGuid()).ToArray();Assert.Equal(Math.Round(own.Sum(p=>Expected(p)??0),2),item.GetProperty("monthlySubscriptionValue").GetDecimal());Assert.Equal(own.Count(p=>Expected(p)==null),item.GetProperty("unpricedSubscriptions").GetInt32());
        }
        var paged=await owner.GetFromJsonAsync<JsonElement>("/api/v1/people/players?asOf=2026-09-28&sort=value_desc&page=1&pageSize=2");Assert.Equal(all.GetArrayLength(),paged.GetProperty("total").GetInt32());Assert.Equal(all.EnumerateArray().Take(2).Select(x=>x.GetProperty("id").GetGuid()),paged.GetProperty("items").EnumerateArray().Select(x=>x.GetProperty("id").GetGuid()));
        var guardians=await owner.GetFromJsonAsync<JsonElement>("/api/v1/people/guardians?asOf=2026-09-28&sort=value_desc&page=1&pageSize=100");
        foreach(var g in guardians.GetProperty("items").EnumerateArray()){
            var id=g.GetProperty("id").GetGuid();var children=await db.GuardianPlayerLinks.Where(l=>l.AcademyId==DemoSeed.NogoomAcademyId&&l.GuardianId==id&&l.IsActive&&l.Guardian.IsActive&&l.Player.IsActive).Select(l=>l.PlayerId).Distinct().ToArrayAsync();
            Assert.Equal(children.Length,g.GetProperty("linkedChildren").GetInt32());Assert.Equal(Math.Round(effective.Where(p=>children.Contains(p.SportEnrollment.PlayerId)).Sum(p=>Expected(p)??0),2),g.GetProperty("monthlySubscriptionValue").GetDecimal());Assert.False(g.TryGetProperty("payerUserId",out _));
        }
        var vip=await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/vip-members?asOf=2026-09-28");Assert.True(vip.GetProperty("items").GetArrayLength()<=10);
        var amounts=vip.GetProperty("items").EnumerateArray().Select(x=>x.GetProperty("monthlySubscriptionValue").GetDecimal()).ToArray();Assert.Equal(amounts.OrderDescending(),amounts);Assert.All(amounts,a=>Assert.True(a>0));
        Assert.Equal(HttpStatusCode.Forbidden,(await coach.GetAsync("/api/v1/reports/vip-members")).StatusCode);
        var other=await foreign.GetFromJsonAsync<JsonElement>($"/api/v1/people/players?asOf=2026-09-28&academyId={DemoSeed.NogoomAcademyId}");Assert.DoesNotContain(other.EnumerateArray(),x=>x.GetProperty("id").GetGuid()==Slice2DemoSeed.OmarPlayerId);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await owner.GetAsync("/api/v1/people/guardians?page=0")).StatusCode);
    }
}
