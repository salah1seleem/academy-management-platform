using Academy.Api.Operations;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V2PlanPublicationTests
{
    [Fact]
    public async Task Capacity_forecast_keeps_incoming_dated_single_session_and_compensation_members()
    {
        using var owner = await Staff();
        var moved = await OccurrenceReady(owner);
        var preview = await SessionReview(owner, moved.F.First, new SessionTransferPair(moved.F.Next, moved.TargetSession));
        (await SessionSave(owner, moved.F.First, preview)).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var single = await DatedGroupRoster.Forecast(db, moved.Target, PlacementToday);
        Assert.Contains(single, row => row.Count == 1);
        Assert.Contains(single, row => row.Count == 0); // no recurring membership was invented

        var dated = await OccurrenceReady(owner);
        (await TransferSave(owner, dated.F.First, await TransferReview(owner, dated.F.First, dated.Target))).EnsureSuccessStatusCode();
        Assert.Contains(await DatedGroupRoster.Forecast(db, dated.Target, PlacementToday), row => row.Count == 1);

        var makeup = await CompensationReady(owner);
        var grant = await CompensationGrant(owner, makeup.F.First, makeup.F.Past);
        var booking = await CompensationBookReview(owner, grant, makeup.TargetSession);
        (await CompensationCommand(owner, $"/api/v1/people/compensations/{grant}/booking", booking)).EnsureSuccessStatusCode();
        var compensated = await DatedGroupRoster.Forecast(db, makeup.Target, PlacementToday);
        Assert.Contains(compensated, row => row.Count == 1);
        Assert.False(await DatedGroupRoster.CanAdd(db, makeup.Target, PlacementToday, 1));
    }

    [Fact]
    public async Task Recommendation_seed_is_independent_repeatable_and_preserves_reviewed_data_without_recollection()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var collections = scope.ServiceProvider.GetRequiredService<CollectionService>();
        await GroupPlacementDemoSeed.Seed(db, collections);
        await PlacementRecommendationDemoSeed.Seed(db, collections);
        var player = await db.Players.SingleAsync(p => p.Id == GroupPlacementDemoSeed.Id(194));
        player.ArabicName = "اسم مراجعة محفوظ — اصطناعي"; await db.SaveChangesAsync();
        var before = (await db.Collections.CountAsync(), await db.Players.CountAsync());
        await PlacementRecommendationDemoSeed.Seed(db, collections); await db.Entry(player).ReloadAsync();
        Assert.Equal("اسم مراجعة محفوظ — اصطناعي", player.ArabicName);
        Assert.Equal(before, (await db.Collections.CountAsync(), await db.Players.CountAsync()));
        var enrollment = await db.SportEnrollments.SingleAsync(e => e.Id == GroupPlacementDemoSeed.Id(195));
        Assert.Null(enrollment.TrainingGroupId);
        Assert.Equal(GroupPlacementDemoSeed.Id(190), enrollment.TrainingLevelId);
        Assert.Equal("InternalTest", await db.Collections.Where(c => c.SportEnrollmentId == enrollment.Id).Select(c => c.PaymentMethod).SingleAsync());
    }
}
