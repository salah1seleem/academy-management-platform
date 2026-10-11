using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class V2PlanPublicationTests
{
    private async Task<Guid> PlacementAlternative(HttpClient owner, PlacementFixture f, int hour, bool full = false, bool wrongLevel = false)
    {
        var id = Guid.NewGuid(); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var source = await db.TrainingGroups.SingleAsync(x => x.Id == f.Group);
        db.TrainingGroups.Add(new() { Id = id, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "بديل سباحة اصطناعي " + id.ToString("N")[..8], BranchId = f.Branch, SportId = source.SportId, AgeCategoryId = source.AgeCategoryId, Capacity = 1, TrainingLevelId = wrongLevel ? null : f.Level });
        db.TrainingSessions.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, TrainingGroupId = id, BranchId = f.Branch, SportId = source.SportId, AgeCategoryId = source.AgeCategoryId, SessionDate = PlacementToday.AddDays(1), StartTime = new(hour, 0), EndTime = new(hour + 1, 0), Source = TrainingSessionSource.Manual, CreatedByUserId = await db.Users.Where(x => x.Email == DemoSeed.OwnerEmail).Select(x => x.Id).SingleAsync() });
        if (full) { var player = Guid.NewGuid(); db.Players.Add(new() { Id = player, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "شاغل مقعد اصطناعي", PlayerCode = "SEAT-" + player.ToString("N")[..10], DateOfBirth = new(2017, 1, 1) }); db.SportEnrollments.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, PlayerId = player, BranchId = f.Branch, SportId = source.SportId, TrainingGroupId = id }); }
        await db.SaveChangesAsync(); await ScheduleCoach(owner, id); return id;
    }
    private static async Task<PlacementSuggestions> Suggest(HttpClient c, Guid enrollment, PlacementPreferences p)
    {
        var r = await Write(c, $"/api/v1/people/enrollments/{enrollment}/placement-recommendations", p); Assert.True(r.IsSuccessStatusCode, await r.Content.ReadAsStringAsync()); return (await r.Content.ReadFromJsonAsync<PlacementSuggestions>())!;
    }
    [Fact]
    public async Task Placement_recommendations_return_top_three_in_explainable_order_only_after_hard_constraints_and_without_writes()
    {
        using var owner = await Staff(); var f = await PlacementReady(owner); var near = await PlacementAlternative(owner, f, 14); var next = await PlacementAlternative(owner, f, 11); var fourth = await PlacementAlternative(owner, f, 18); var full = await PlacementAlternative(owner, f, 14, full: true); var wrong = await PlacementAlternative(owner, f, 14, wrongLevel: true);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var cash = await db.Collections.CountAsync(); var att = await db.PlayerAttendances.CountAsync(); var preferred = new PlacementPreferences([(int)PlacementToday.AddDays(1).DayOfWeek], new(14, 0), f.Coach);
        var result = await Suggest(owner, f.First, preferred); Assert.Equal([near, next, f.Group], result.Choices.Select(x => x.GroupId)); Assert.Equal([1, 2, 3], result.Choices.Select(x => x.Rank)); Assert.DoesNotContain(result.Choices, c => c.GroupId == full || c.GroupId == wrong || c.GroupId == fourth); Assert.Contains(result.Choices[0].Reasons, x => x.Contains("0 دقيقة")); Assert.Contains(result.Choices[2].Reasons, x => x.Contains("المدرب المفضل"));
        var retry = await Suggest(owner, f.First, preferred); Assert.Equal(result.Choices.Select(x => x.GroupId), retry.Choices.Select(x => x.GroupId)); Assert.Equal(cash, await db.Collections.CountAsync()); Assert.Equal(att, await db.PlayerAttendances.CountAsync()); Assert.False(await db.GroupPlacementRevisions.AnyAsync(x => x.TrainingGroupId == f.Group)); Assert.Null(await db.SportEnrollments.Where(x => x.Id == f.First).Select(x => x.TrainingGroupId).SingleAsync());
    }
    [Fact]
    public async Task Placement_recommendations_never_break_subscription_or_level_constraints_when_no_alternative_exists()
    {
        using var owner = await Staff(); var f = await PlacementReady(owner, secondPaid: false); var unpaid = await Suggest(owner, f.Second, new()); Assert.Empty(unpaid.Choices); Assert.Contains("الانتظار", unpaid.NoAlternativeMessage);
        (await Write(owner, $"/api/v1/people/enrollments/{f.First}/placement-level", new PlacementLevelWrite(null), true)).EnsureSuccessStatusCode(); var noLevel = await Suggest(owner, f.First, new()); Assert.Empty(noLevel.Choices);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, $"/api/v1/people/enrollments/{f.First}/placement-recommendations", new PlacementPreferences([6, 6]))).StatusCode); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, $"/api/v1/people/enrollments/{f.First}/placement-recommendations", new PlacementPreferences([7]))).StatusCode);
    }
    [Fact]
    public async Task Placement_recommendation_is_not_a_seat_reservation_and_handoff_filters_only_the_selected_visible_enrollment()
    {
        using var owner = await Staff(); var f = await PlacementReady(owner, 1); var result = await Suggest(owner, f.First, new()); Assert.Equal(f.Group, Assert.Single(result.Choices).GroupId); var p = await ReviewPlacement(owner, f.Group, f.Second); (await Place(owner, f.Group, p)).EnsureSuccessStatusCode(); var stale = await ReviewPlacement(owner, f.Group, f.First); Assert.NotEmpty(stale.Conflicts); Assert.Equal(HttpStatusCode.Conflict, (await Place(owner, f.Group, stale)).StatusCode); Assert.Empty((await Suggest(owner, f.First, new())).Choices);
        var focused = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/groups/{f.Group}/placement-candidates?enrollmentId={f.First}"); Assert.Equal(f.First, Assert.Single(focused.GetProperty("players").EnumerateArray()).GetProperty("id").GetGuid()); Assert.Empty((await owner.GetFromJsonAsync<JsonElement>($"/api/v1/manage/groups/{f.Group}/placement-candidates?enrollmentId={Guid.NewGuid()}")).GetProperty("players").EnumerateArray());
    }
    [Fact]
    public async Task Placement_recommendations_require_current_read_permissions_branch_tenant_and_CSRF_and_hide_unavailable_coaches()
    {
        using var owner = await Staff(); var f = await PlacementReady(owner); using var foreign = await Staff("owner.future@example.test"); using var guardian = await Guardian(); var path = $"/api/v1/people/enrollments/{f.First}/placement-recommendations"; Assert.Equal(HttpStatusCode.NotFound, (await Write(foreign, path, new PlacementPreferences())).StatusCode); Assert.Equal(HttpStatusCode.Forbidden, (await Write(guardian, path, new PlacementPreferences())).StatusCode); Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync(path, new PlacementPreferences())).StatusCode); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, path, new PlacementPreferences(PreferredCoachId: Guid.NewGuid()))).StatusCode);
        var email = $"suggest-{Guid.NewGuid():N}@example.test"; var staff = await Write(owner, "/api/v1/manage/staff", new StaffWrite("إداري اقتراح تجريبي", email, "010" + Random.Shared.Next(10000000, 99999999), "Demo-Only-123!", AcademyRole.AcademyAdmin, null)); staff.EnsureSuccessStatusCode(); var member = (await staff.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid(); var access = new StaffAccessWrite(false, [f.OtherBranch], StaffAccessRules.Modules.Select(m => new StaffActionValue(m, m is "players" or "academy", false, false, false)).ToArray()); (await Write(owner, $"/api/v1/manage/staff/{member}/access", access, true)).EnsureSuccessStatusCode(); using var admin = await Staff(email); Assert.Equal(HttpStatusCode.NotFound, (await Write(admin, path, new PlacementPreferences())).StatusCode); (await Write(owner, $"/api/v1/manage/staff/{member}/access", access with { Branches = [f.Branch] }, true)).EnsureSuccessStatusCode(); Assert.Single((await Suggest(admin, f.First, new())).Choices); var options = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/people/enrollments/{f.First}/placement-preferences"); Assert.Equal(f.Coach, Assert.Single(options.GetProperty("coaches").EnumerateArray()).GetProperty("id").GetGuid());
    }
}
