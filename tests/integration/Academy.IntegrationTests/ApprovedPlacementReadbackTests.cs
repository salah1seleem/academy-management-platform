using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Academy.IntegrationTests;

public sealed partial class V2PlanPublicationTests
{
    [Fact]
    public async Task Approved_compensation_readback_identifies_original_occurrence_and_reserved_booking_without_financial_side_effects()
    {
        using var owner=await Staff();var f=await CompensationReady(owner);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var before=(await db.Collections.CountAsync(),await db.PlayerAttendances.CountAsync(),await db.SubscriptionPeriods.Where(p=>p.SportEnrollmentId==f.F.First).Select(p=>p.RemainingSessions).SingleAsync());
        var grant=await CompensationGrant(owner,f.F.First,f.F.Past);
        var body=await CompensationBookReview(owner,grant,f.TargetSession);
        var response=await CompensationCommand(owner,$"/api/v1/people/compensations/{grant}/booking",body);response.EnsureSuccessStatusCode();
        var id=(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var path=$"/api/v1/people/enrollments/{f.F.First}/compensations";
        var read=await owner.GetFromJsonAsync<JsonElement>(path);
        var source=Assert.Single(read.EnumerateArray(),g=>g.GetProperty("id").GetGuid()==grant);
        Assert.Equal(f.F.Past,source.GetProperty("sourceSessionId").GetGuid());Assert.Equal(f.F.First,source.GetProperty("sportEnrollmentId").GetGuid());
        Assert.Equal("CancelledSession",source.GetProperty("cause").GetString());Assert.Equal("Session",source.GetProperty("unit").GetString());Assert.Equal(1,source.GetProperty("units").GetInt32());
        var booking=Assert.Single(source.GetProperty("bookings").EnumerateArray());
        Assert.Equal(id,booking.GetProperty("id").GetGuid());Assert.Equal(f.TargetSession,booking.GetProperty("trainingSessionId").GetGuid());Assert.Equal("Reserved",booking.GetProperty("state").GetString());Assert.Equal(1,booking.GetProperty("units").GetInt32());
        using var foreign=await Staff(DemoSeed.FutureOwnerEmail);using var guardian=await Guardian();
        Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await guardian.GetAsync(path)).StatusCode);
        Assert.Equal(before,(await db.Collections.CountAsync(),await db.PlayerAttendances.CountAsync(),await db.SubscriptionPeriods.Where(p=>p.SportEnrollmentId==f.F.First).Select(p=>p.RemainingSessions).SingleAsync()));
    }
}
