using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Api.Slice4;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;
public sealed partial class V2StaffScopeTests
{
    private static PlayerUpdateRequest IdentityBody(Player p) => new(p.ArabicName, p.EnglishName,
        p.DateOfBirth, p.Gender, p.HeightCm, p.WeightKg, p.PreferredFoot, p.FootballPosition, p.Address);

    [Fact]
    public async Task Approved_player_edit_rejects_future_or_missing_birth_without_changing_identity_or_links()
    {
        using var owner = await Staff(DemoSeed.OwnerEmail);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var original = await db.Players.AsNoTracking().Where(p => p.AcademyId == DemoSeed.NogoomAcademyId &&
            db.GuardianPlayerLinks.Any(l => l.PlayerId == p.Id)).FirstAsync();
        var body = IdentityBody(original);
        var enrollments = await db.SportEnrollments.Where(e => e.PlayerId == original.Id).Select(e => e.Id).OrderBy(x => x).ToArrayAsync();
        var links = await db.GuardianPlayerLinks.Where(l => l.PlayerId == original.Id).Select(l => l.Id).OrderBy(x => x).ToArrayAsync();
        var collections = await db.Collections.CountAsync();
        var today = await AttendanceCalendar.TodayAsync(original.AcademyId, db, TimeProvider.System);
        try
        {
            foreach (var date in new[] { today.AddYears(1), DateOnly.MinValue })
            {
                var response = await Write(owner, $"/api/v1/people/players/{original.Id}", body with { DateOfBirth = date });
                Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
                var error = await response.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Contains("تاريخ الميلاد", error.GetProperty("errors").GetProperty("dateOfBirth")[0].GetString());
                var read = await db.Players.AsNoTracking().SingleAsync(p => p.Id == original.Id);
                Assert.Equal(body, IdentityBody(read));
            }
            Assert.Equal(enrollments, await db.SportEnrollments.Where(e => e.PlayerId == original.Id).Select(e => e.Id).OrderBy(x => x).ToArrayAsync());
            Assert.Equal(links, await db.GuardianPlayerLinks.Where(l => l.PlayerId == original.Id).Select(l => l.Id).OrderBy(x => x).ToArrayAsync());
            Assert.Equal(collections, await db.Collections.CountAsync());
        }
        finally
        {
            (await Write(owner, $"/api/v1/people/players/{original.Id}", body)).EnsureSuccessStatusCode();
        }
    }

    [Fact]
    public async Task Approved_player_identity_edit_requires_named_grant_and_scoped_branch_without_creating_a_player()
    {
        var f = await CreateAdmin(); using var owner = f.Owner; using var admin = f.Admin;
        var data = await Data();
        var access = Access(data.B, edit: true) with { AllSports = true, Sports = [], WorkflowActions = ["workflow.players.read", "workflow.players.edit"] };
        (await Write(owner, $"/api/v1/manage/staff/{f.Member}/access", access)).EnsureSuccessStatusCode();
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var original = await db.Players.AsNoTracking().SingleAsync(p => p.Id == data.PlayerB);
        var body = IdentityBody(original);
        var players = await db.Players.CountAsync(); var money = await db.Collections.CountAsync();
        var enrollmentIds = await db.SportEnrollments.Where(e => e.PlayerId == original.Id).Select(e => e.Id).ToArrayAsync();
        try
        {
            var change = body with { ArabicName = "لاعب تعديل هوية اصطناعي", EnglishName = "Synthetic Player", DateOfBirth = new DateOnly(2016, 1, 2) };
            (await Write(admin, $"/api/v1/people/players/{original.Id}", change)).EnsureSuccessStatusCode();
            var read = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/people/players/{original.Id}");
            Assert.Equal(original.Id, read.GetProperty("id").GetGuid());
            Assert.Equal(change.ArabicName, read.GetProperty("arabicName").GetString());
            Assert.Equal(change.EnglishName, read.GetProperty("englishName").GetString());
            Assert.Equal("2016-01-02", read.GetProperty("dateOfBirth").GetString());
            (await Write(owner, $"/api/v1/manage/staff/{f.Member}/access", access with { WorkflowActions = ["workflow.players.read"] })).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, $"/api/v1/people/players/{original.Id}", body)).StatusCode);
            (await Write(owner, $"/api/v1/manage/staff/{f.Member}/access", access with { Branches = [data.A] })).EnsureSuccessStatusCode();
            Assert.Equal(HttpStatusCode.NotFound, (await Write(admin, $"/api/v1/people/players/{original.Id}", body)).StatusCode);
            using var other = await Staff(DemoSeed.FutureOwnerEmail);
            Assert.Equal(HttpStatusCode.NotFound, (await Write(other, $"/api/v1/people/players/{original.Id}", body)).StatusCode);
            Assert.Equal(players, await db.Players.CountAsync()); Assert.Equal(money, await db.Collections.CountAsync());
            Assert.Equal(enrollmentIds, await db.SportEnrollments.Where(e => e.PlayerId == original.Id).Select(e => e.Id).ToArrayAsync());
        }
        finally { (await Write(owner, $"/api/v1/people/players/{original.Id}", body)).EnsureSuccessStatusCode(); }
    }
}
