using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Facilities;
using Academy.Api.Operations;
using Academy.Api.Slice3;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class V2PlanPublicationTests
{
    [Fact]
    public async Task Group_creation_demo_seed_is_repeatable_preserves_review_edits_and_never_recollects()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var collections = scope.ServiceProvider.GetRequiredService<CollectionService>(); await GroupPlacementDemoSeed.Seed(db, collections); await GroupCreationDemoSeed.Seed(db, collections); var player = await db.Players.SingleAsync(p => p.Id == GroupPlacementDemoSeed.Id(100)); player.ArabicName = "اسم مراجع اصطناعي محفوظ"; await db.SaveChangesAsync(); var before = (await db.Collections.CountAsync(), await db.Players.CountAsync()); await GroupCreationDemoSeed.Seed(db, collections); await db.Entry(player).ReloadAsync(); Assert.Equal("اسم مراجع اصطناعي محفوظ", player.ArabicName); Assert.Equal(before, (await db.Collections.CountAsync(), await db.Players.CountAsync())); Assert.Null(await db.SportEnrollments.Where(e => e.Id == GroupPlacementDemoSeed.Id(101)).Select(e => e.TrainingGroupId).SingleAsync());
    }
    private async Task<(PlacementFixture Fixture, GroupCreationWrite Body)> NewGroupReady(HttpClient owner, bool paid = true)
    {
        var f = await PlacementReady(owner, secondPaid: paid); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var category = await db.TrainingGroups.Where(g => g.Id == f.Group).Select(g => g.AgeCategoryId).SingleAsync(); var day = PlacementToday.AddDays(2);
        return (f, new("مجموعة متكاملة اصطناعية " + Guid.NewGuid().ToString("N")[..8], f.Branch, Academy.Api.Slice2.Slice2DemoSeed.SwimmingId, category, f.Level, 2, TrainingRegistrationFormat.Group, false, f.Coach, new(day, day, [day.DayOfWeek], new(13, 0), new(14, 0)), [f.First, f.Second]));
    }
    private static async Task<GroupCreationPreview> CreationReview(HttpClient owner, GroupCreationWrite b)
    {
        var response = await Write(owner, "/api/v1/manage/group-creation/preview", b); Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); return (await response.Content.ReadFromJsonAsync<GroupCreationPreview>())!;
    }
    private static async Task<HttpResponseMessage> CreationSave(HttpClient c, GroupCreationWrite b, GroupCreationPreview review, string? key = null) => await CompensationCommand(c, "/api/v1/manage/group-creation", b with { Fingerprint = review.Fingerprint }, key);
    [Fact]
    public async Task Group_creation_preview_is_nonpersistent_then_group_coach_schedule_members_commit_once_without_finance()
    {
        using var owner = await Staff(); var f = await NewGroupReady(owner); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var before = (await db.TrainingGroups.CountAsync(), await db.TrainingSessions.CountAsync(), await db.Collections.CountAsync(), await db.PlayerAttendances.CountAsync());
        var review = await CreationReview(owner, f.Body); Assert.Empty(review.Conflicts); Assert.Equal(1, review.Sessions); Assert.Equal(2, review.Players); Assert.Equal(before, (await db.TrainingGroups.CountAsync(), await db.TrainingSessions.CountAsync(), await db.Collections.CountAsync(), await db.PlayerAttendances.CountAsync())); Assert.Null(await db.SportEnrollments.Where(e => e.Id == f.Fixture.First).Select(e => e.TrainingGroupId).SingleAsync());
        var key = Guid.NewGuid().ToString(); var response = await CreationSave(owner, f.Body, review, key); Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        Assert.Equal(2, await db.SportEnrollments.CountAsync(e => e.TrainingGroupId == id)); Assert.Single(await db.TrainingSessions.Where(s => s.TrainingGroupId == id).ToArrayAsync()); Assert.Equal(f.Body.CoachMembershipId, await db.StaffGroupAssignments.Where(s => s.TrainingGroupId == id).Select(s => s.AcademyMembershipId).SingleAsync()); Assert.Equal(before.Item3, await db.Collections.CountAsync()); Assert.Equal(before.Item4, await db.PlayerAttendances.CountAsync());
        Assert.True((await (await CreationSave(owner, f.Body, review, key)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replayed").GetBoolean()); Assert.Equal(HttpStatusCode.Conflict, (await CreationSave(owner, f.Body with { ArabicName = "اسم آخر" }, review, key)).StatusCode);
        using var guardian = await Guardian(); var profile = await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/children/{await PlayerFor(f.Fixture.First)}/profile"); var enrollment = Assert.Single(profile.GetProperty("enrollments").EnumerateArray()); Assert.Equal(f.Body.ArabicName, enrollment.GetProperty("group").GetString()); Assert.Equal(f.Body.ArabicName, Assert.Single(enrollment.GetProperty("upcoming").EnumerateArray()).GetProperty("group").GetString());
    }
    [Fact]
    public async Task Group_creation_resource_preview_rolls_back_then_commit_reserves_once_and_conflicting_creation_rolls_back()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services); using var owner = await Staff(); var f = await NewGroupReady(owner); var resource = await FacilityFixture(owner, branch: f.Body.BranchId);
        var body = f.Body with { Timetable = new(FacilityDay, FacilityDay, [FacilityDay.DayOfWeek], new(17, 0), new(18, 0), new(resource, f.Body.CoachMembershipId, 2, false)) };
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var before = await db.ResourceBookings.CountAsync(); var review = await CreationReview(owner, body); Assert.Empty(review.Conflicts); Assert.NotNull(review.Resource); Assert.Equal(before, await db.ResourceBookings.CountAsync());
        var key = Guid.NewGuid().ToString(); var response = await CreationSave(owner, body, review, key); Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync()); var id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); Assert.Equal(before + 1, await db.ResourceBookings.CountAsync()); Assert.Single(await db.GroupResourceSchedules.Where(s => s.TrainingGroupId == id).ToArrayAsync());
        (await CreationSave(owner, body, review, key)).EnsureSuccessStatusCode(); Assert.Equal(before + 1, await db.ResourceBookings.CountAsync());
        var conflicting = body with { ArabicName = body.ArabicName + " متعارضة", EnrollmentIds = [] }; var rejected = await CreationReview(owner, conflicting); Assert.NotEmpty(rejected.Conflicts); Assert.Equal(HttpStatusCode.Conflict, (await CreationSave(owner, conflicting, rejected)).StatusCode); Assert.False(await db.TrainingGroups.AnyAsync(g => g.ArabicName == conflicting.ArabicName)); Assert.Equal(before + 1, await db.ResourceBookings.CountAsync());
    }
    [Fact]
    public async Task Group_creation_unpaid_member_blocks_the_whole_group_and_empty_group_is_allowed()
    {
        using var owner = await Staff(); var f = await NewGroupReady(owner, false); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var before = await db.TrainingGroups.CountAsync();
        var rejected = await CreationReview(owner, f.Body); Assert.Contains(rejected.Conflicts, c => c.Contains("استحقاق")); Assert.Equal(HttpStatusCode.Conflict, (await CreationSave(owner, f.Body, rejected)).StatusCode); Assert.Equal(before, await db.TrainingGroups.CountAsync()); Assert.False(await db.TrainingGroups.AnyAsync(g => g.ArabicName == f.Body.ArabicName)); Assert.Null(await db.SportEnrollments.Where(e => e.Id == f.Fixture.First).Select(e => e.TrainingGroupId).SingleAsync());
        var empty = f.Body with { EnrollmentIds = [] }; var review = await CreationReview(owner, empty); Assert.Empty(review.Conflicts); (await CreationSave(owner, empty, review)).EnsureSuccessStatusCode(); Assert.Equal(before + 1, await db.TrainingGroups.CountAsync());
    }
    [Fact]
    public async Task Group_creation_rechecks_stale_eligibility_and_serializes_competing_member_assignments()
    {
        using var owner = await Staff(); var f = await NewGroupReady(owner); var first = f.Body with { EnrollmentIds = [f.Fixture.First], Capacity = 1 }; var a = await CreationReview(owner, first); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var player = await db.Players.SingleAsync(p => p.Id == (db.SportEnrollments.Where(e => e.Id == f.Fixture.First).Select(e => e.PlayerId).Single())); player.ArabicName += " محدث"; await db.SaveChangesAsync(); Assert.Equal(HttpStatusCode.Conflict, (await CreationSave(owner, first, a)).StatusCode);
        var second = first with { ArabicName = first.ArabicName + " ب", Timetable = first.Timetable with { StartTime = new(15, 0), EndTime = new(16, 0) } }; a = await CreationReview(owner, first); var b = await CreationReview(owner, second);
        var responses = await Task.WhenAll(CreationSave(owner, first, a), CreationSave(owner, second, b)); Assert.Single(responses, r => r.IsSuccessStatusCode); Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict); Assert.Equal(1, await db.TrainingGroups.CountAsync(g => g.ArabicName == first.ArabicName || g.ArabicName == second.ArabicName));
    }
    [Fact]
    public async Task Group_creation_enforces_scope_roles_CSRF_private_capacity_resource_and_bounded_candidates()
    {
        using var owner = await Staff(); var f = await NewGroupReady(owner); using var foreign = await Staff("owner.future@example.test"); using var guardian = await Guardian(); using var coach = factory.CreateClient(); await MobileCoachTestSession.SignInAsync(coach, factory.Services);
        var path = "/api/v1/manage/group-creation/preview"; Assert.Equal(HttpStatusCode.NotFound, (await Write(foreign, path, f.Body)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Write(guardian, path, f.Body)).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Write(coach, path, f.Body)).StatusCode); Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path, f.Body)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, path, f.Body with { RegistrationFormat = TrainingRegistrationFormat.Private, CapacityReviewed = true })).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, path, f.Body with { EnrollmentIds = [f.Fixture.First, f.Fixture.First] })).StatusCode);
        var list = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/group-creation/candidates?branchId={f.Body.BranchId}&sportId={f.Body.SportId}"); Assert.Contains(list.GetProperty("players").EnumerateArray(), p => p.GetProperty("id").GetGuid() == f.Fixture.First);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.GetAsync($"/api/v1/manage/group-creation/candidates?branchId={f.Body.BranchId}&sportId={f.Body.SportId}&offset=-1")).StatusCode);
    }
}
