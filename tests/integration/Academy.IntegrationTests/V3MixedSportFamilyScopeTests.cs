using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class SubscriptionPaymentTests
{
    [Fact]
    public async Task V3_mixed_sport_family_request_and_its_proof_require_every_sport_not_one_visible_child()
    {
        await using var f = await NewFamily(); var swimming = Guid.NewGuid(); var swimmingGroup = Guid.NewGuid();
        Guid player; Guid[] sharedGroupPlayers;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var now = DateTimeOffset.UtcNow;
            player = await db.SportEnrollments.Where(e => e.Id == f.Choices[0].SportEnrollmentId).Select(e => e.PlayerId).SingleAsync();
            var sharedGroup = await db.TrainingGroups.SingleAsync(g => g.Id == Slice2DemoSeed.SwimmingGroupId);
            sharedGroupPlayers = await db.SportEnrollments.Where(e => e.TrainingGroupId == sharedGroup.Id).Select(e => e.PlayerId).Order().ToArrayAsync();
            // Keep the common regression group's roster stable across test execution orders.
            db.TrainingGroups.Add(new TrainingGroup { Id = swimmingGroup, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "سباحة الأسرة الاصطناعية " + swimmingGroup.ToString("N")[..8], SportId = sharedGroup.SportId, BranchId = sharedGroup.BranchId, AgeCategoryId = sharedGroup.AgeCategoryId, Capacity = 10, CreatedAtUtc = now, UpdatedAtUtc = now });
            db.SportEnrollments.Add(new() { Id = swimming, AcademyId = DemoSeed.NogoomAcademyId, PlayerId = player, SportId = sharedGroup.SportId, BranchId = sharedGroup.BranchId, TrainingGroupId = swimmingGroup, CreatedAtUtc = now, UpdatedAtUtc = now });
            await db.SaveChangesAsync();
        }
        var mixed = f with { Choices = [.. f.Choices, new(swimming, Slice3DemoSeed.SwimmingMonthlyId)] };
        var request = await FamilyProof(mixed, await CreateFamily(mixed)); var id = request.GetProperty("id").GetGuid();
        using var owner = await InboxStaff(DemoSeed.OwnerEmail); var email = $"sports.family.{Guid.NewGuid():N}@example.test";
        var created = await Post(owner, "/api/v1/manage/staff", new StaffWrite("إداري رياضة للأسرة الاصطناعية", email, "010" + Random.Shared.Next(10000000, 99999999), "Demo-Only-123!", AcademyRole.AcademyAdmin, "اختبار نطاق رياضي"), await Csrf(owner));
        created.EnsureSuccessStatusCode(); var member = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var actions = StaffAccessRules.Modules.Select(m => new StaffActionValue(m, m is "payments" or "subscriptions", false, m == "payments", false)).ToArray();
        async Task Grant(Guid[] sports)
        {
            using var message = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/manage/staff/{member}/access") { Content = JsonContent.Create(new StaffAccessWrite(true, [], actions, AllSports: false, Sports: sports)) };
            message.Headers.Add("X-CSRF-TOKEN", await Csrf(owner)); (await owner.SendAsync(message)).EnsureSuccessStatusCode();
        }
        await Grant([Slice2DemoSeed.FootballId]); using var admin = await InboxStaff(email);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{FamilyStaff}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(request.GetProperty("proofUrl").GetString())).StatusCode);
        var queue = await admin.GetFromJsonAsync<JsonElement>("/api/v1/manage/payment-requests?allDates=true");
        Assert.DoesNotContain(queue.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
        await Grant([Slice2DemoSeed.FootballId, Slice2DemoSeed.SwimmingId]);
        Assert.True((await admin.GetFromJsonAsync<JsonElement>($"{FamilyStaff}/{id}")).GetProperty("canReview").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(request.GetProperty("proofUrl").GetString())).StatusCode);
        await Grant([Slice2DemoSeed.FootballId]);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{FamilyStaff}/{id}")).StatusCode);
        await using var verifyScope = factory.Services.CreateAsyncScope(); var verify = verifyScope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.False(await verify.FamilyRenewalLines.AnyAsync(l => l.RequestId == id && l.ResultPaymentId != null));
        Assert.Equal(sharedGroupPlayers, await verify.SportEnrollments.Where(e => e.TrainingGroupId == Slice2DemoSeed.SwimmingGroupId).Select(e => e.PlayerId).Order().ToArrayAsync());
        var sharedRoster = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/people/players?groupId={Slice2DemoSeed.SwimmingGroupId}");
        Assert.DoesNotContain(sharedRoster.EnumerateArray(), p => p.GetProperty("id").GetGuid() == player);
        var familyRoster = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/people/players?groupId={swimmingGroup}");
        Assert.Equal(player, Assert.Single(familyRoster.EnumerateArray()).GetProperty("id").GetGuid());
    }
}
