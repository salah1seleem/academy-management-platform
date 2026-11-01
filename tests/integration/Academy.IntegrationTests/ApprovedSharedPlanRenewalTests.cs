using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.People;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Academy.IntegrationTests;
public sealed partial class SubscriptionPaymentTests
{
    [Fact]
    public async Task Shared_plan_mode_rejects_single_child_different_plans_and_mixed_sports_without_financial_writes()
    {
        await using var family = await NewFamily();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var before = (await db.Collections.CountAsync(), await db.FamilyRenewalRequests.CountAsync(), await db.Receipts.CountAsync());
        async Task<HttpResponseMessage> Quote(FamilyRenewalChoice[] choices, bool shared = true) => await Post(family.Client, FamilyQuotes+"/quote", new FamilyRenewalWrite(choices, shared), await Csrf(family.Client));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Quote([family.Choices[0]])).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Quote([family.Choices[0], family.Choices[1] with { SubscriptionPlanId = Slice3DemoSeed.FootballQuarterlyId }])).StatusCode);
        var enrollment = await db.SportEnrollments.SingleAsync(e => e.Id == family.Choices[1].SportEnrollmentId);
        var group = await db.TrainingGroups.FirstAsync(g => g.AcademyId == DemoSeed.NogoomAcademyId && g.SportId == Slice2DemoSeed.SwimmingId);
        var swimming = new SportEnrollment { Id = Guid.NewGuid(), AcademyId = enrollment.AcademyId, PlayerId = enrollment.PlayerId, BranchId = group.BranchId, SportId = group.SportId, TrainingGroupId = group.Id, CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow };
        db.SportEnrollments.Add(swimming); await db.SaveChangesAsync();
        var mixed = new[] { family.Choices[0], new FamilyRenewalChoice(swimming.Id, Slice3DemoSeed.SwimmingMonthlyId) };
        var rejection = await Quote(mixed); Assert.Equal(HttpStatusCode.UnprocessableEntity, rejection.StatusCode);
        Assert.Contains("الرياضة نفسها", await rejection.Content.ReadAsStringAsync());
        // Preserve the pre-existing authorized contract; the new native mode narrows its own quote/request.
        (await Quote(mixed, false)).EnsureSuccessStatusCode();
        Assert.Equal(before, (await db.Collections.CountAsync(), await db.FamilyRenewalRequests.CountAsync(), await db.Receipts.CountAsync()));
    }
    [Fact]
    public async Task Shared_plan_policy_is_bound_to_quote_and_retry_hash_without_changing_legacy_pending_requests()
    {
        await using var family = await NewFamily();
        var legacy = await FamilyBody(family); var strict = await FamilyBody(family, true);
        Assert.NotEqual(legacy.QuoteHash, strict.QuoteHash);
        Assert.Equal(HttpStatusCode.Conflict, (await Post(family.Client, FamilyRequests, legacy with { SharedPlanOnly = true }, await Csrf(family.Client))).StatusCode);
        var pending = await CreateFamily(family, strict);
        Assert.Equal(pending.GetProperty("id").GetGuid(), (await CreateFamily(family, strict)).GetProperty("id").GetGuid());
        Assert.Equal(HttpStatusCode.Conflict, (await Post(family.Client, FamilyRequests, strict with { SharedPlanOnly = false }, await Csrf(family.Client))).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var enrollments = family.Choices.Select(c=>c.SportEnrollmentId).ToArray();
        Assert.False(await db.Collections.AnyAsync(c=>enrollments.Contains(c.SportEnrollmentId)));
        Assert.False(await db.SubscriptionPeriods.AnyAsync(p=>enrollments.Contains(p.SportEnrollmentId)));
    }
}
