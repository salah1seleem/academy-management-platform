using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Mobile;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class V3GuardianHomeWorkspaceTests
{
    private sealed class DemoClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);
    }
    private sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder b)
        {
            b.UseEnvironment("Demo");
            b.UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Separate test database required"));
            b.UseSetting("Demo:SeedProfile", "LegacyRegression"); b.UseSetting("Demo:SeedEnabled", "true");
            b.UseSetting("Demo:FixedOtpEnabled", "true"); b.UseSetting("Demo:FixedOtp", "246810");
            b.UseSetting("Demo:StaffPassword", "Demo-Only-123!"); b.UseSetting("Demo:ReferenceDate", "2026-09-28");
            b.ConfigureServices(s => { s.RemoveAll<TimeProvider>(); s.AddSingleton<TimeProvider>(new DemoClock()); });
        }
    }

    [Fact]
    public async Task Guardian_home_keeps_siblings_sessions_and_latest_published_sport_separate_and_never_leaks_drafts_or_other_families()
    {
        await using var factory = new Factory(); using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/guardian/home")).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var member = await db.AcademyMemberships.Include(m => m.User).SingleAsync(m => m.AcademyId == DemoSeed.NogoomAcademyId && m.Role == AcademyRole.Guardian && m.User.PhoneNumber == DemoSeed.GuardianPhone);
        var guardian = await db.GuardianProfiles.SingleAsync(g => g.AcademyId == member.AcademyId && g.UserId == member.UserId);
        var token = MobileAuthenticationHandler.NewToken(); var now = new DemoClock().GetUtcNow();
        db.MobileSessions.Add(new MobileSession { Id = Guid.NewGuid(), UserId = member.UserId, MembershipId = member.Id, AccessTokenHash = MobileAuthenticationHandler.Hash(token), RefreshTokenHash = MobileAuthenticationHandler.Hash(MobileAuthenticationHandler.NewToken()), SecurityStamp = member.User.SecurityStamp ?? "", DeviceName = "ولي أمر تجربة V3 معزولة", CreatedAtUtc = now, LastUsedAtUtc = now, AccessExpiresAtUtc = now.AddMinutes(10), ExpiresAtUtc = now.AddHours(1) });
        var originals = await db.TrainingGroups.Where(g => g.AcademyId == member.AcademyId).ToArrayAsync();
        var football = originals.First(g => g.SportId == Academy.Api.Slice2.Slice2DemoSeed.FootballId);
        var swimming = originals.First(g => g.SportId == Academy.Api.Slice2.Slice2DemoSeed.SwimmingId);
        Player Player(string name) => new() { Id = Guid.NewGuid(), AcademyId = member.AcademyId, PlayerCode = "GH3-" + Guid.NewGuid().ToString("N")[..12], ArabicName = name, DateOfBirth = new(2018, 1, 1) };
        TrainingGroup Group(TrainingGroup source) => new() { Id = Guid.NewGuid(), AcademyId = source.AcademyId, ArabicName = "مجموعة أسرة اختبار " + Guid.NewGuid(), SportId = source.SportId, BranchId = source.BranchId, AgeCategoryId = source.AgeCategoryId };
        SportEnrollment Enrollment(Player p, TrainingGroup g) => new() { Id = Guid.NewGuid(), AcademyId = member.AcademyId, PlayerId = p.Id, SportId = g.SportId, BranchId = g.BranchId, TrainingGroupId = g.Id, Status = EnrollmentStatus.Active };
        var first = Player("لينا رحلة V3"); var second = Player("آدم رحلة V3"); var privateChild = Player("طفل أسرة غير مرتبطة V3");
        var f = Group(football); var s = Group(swimming); var f2 = Group(football);
        var e1 = Enrollment(first, f); var swim = Enrollment(first, s); var e2 = Enrollment(second, f2); var hidden = Enrollment(privateChild, f);
        db.Players.AddRange(first, second, privateChild); db.TrainingGroups.AddRange(f, s, f2); db.SportEnrollments.AddRange(e1, swim, e2, hidden);
        foreach (var p in new[] { first, second }) db.GuardianPlayerLinks.Add(new GuardianPlayerLink { Id = Guid.NewGuid(), AcademyId = member.AcademyId, GuardianId = guardian.Id, PlayerId = p.Id, CreatedByUserId = member.UserId });
        var today = new DateOnly(2026, 10, 15);
        TrainingSession Session(TrainingGroup g, DateOnly date, int hour, TrainingSessionStatus status = TrainingSessionStatus.Scheduled) => new() { Id = Guid.NewGuid(), AcademyId = member.AcademyId, TrainingGroupId = g.Id, BranchId = g.BranchId, SportId = g.SportId, AgeCategoryId = g.AgeCategoryId, SessionDate = date, StartTime = new(hour, 0), EndTime = new(hour + 1, 0), Status = status, Source = TrainingSessionSource.Manual, CreatedByUserId = member.UserId };
        var ended = Session(f, today, 10); var next = Session(f, today, 17); var siblingNext = Session(f2, today.AddDays(2), 18); var swimNext = Session(s, today.AddDays(1), 16);
        db.TrainingSessions.AddRange(ended, next, siblingNext, swimNext, Session(f, today, 16, TrainingSessionStatus.Cancelled));
        PlayerEvaluation Evaluation(SportEnrollment e, TrainingGroup g, DateOnly date, EvaluationStatus status) => new() { Id = Guid.NewGuid(), AcademyId = member.AcademyId, SportEnrollmentId = e.Id, SportId = g.SportId, TrainingGroupId = g.Id, EvaluatedByUserId = member.UserId, EvaluationDate = date, ReportingPeriod = "متابعة عربية اصطناعية", Status = status, PublishedAtUtc = status == EvaluationStatus.Published ? now : null, PublishedByUserId = status == EvaluationStatus.Published ? member.UserId : null, GeneralNotes = "NOTE-NOT-FOR-HOME" };
        var footballReport = Evaluation(e1, f, today.AddDays(-3), EvaluationStatus.Published);
        var latest = Evaluation(swim, s, today.AddDays(-2), EvaluationStatus.Published);
        var draft = Evaluation(e1, f, today, EvaluationStatus.Draft);
        var unlinkedReport = Evaluation(hidden, f, today.AddDays(1), EvaluationStatus.Published);
        var siblingReport = Evaluation(e2, f2, today.AddDays(-1), EvaluationStatus.Published);
        db.PlayerEvaluations.AddRange(footballReport, latest, draft, unlinkedReport, siblingReport);
        var criterion = await db.EvaluationCriteria.FirstAsync(c => c.AcademyId == member.AcademyId && c.SportId == f.SportId && c.FootballAxis != null);
        db.EvaluationScores.Add(new EvaluationScore { Id = Guid.NewGuid(), AcademyId = member.AcademyId, PlayerEvaluationId = footballReport.Id, SportId = f.SportId, EvaluationCriterionId = criterion.Id, CriterionNameSnapshot = criterion.ArabicName, WeightSnapshot = 1, FootballAxisSnapshot = criterion.FootballAxis, Score = 80 });
        var before = (await db.Collections.CountAsync(), await db.SubscriptionPeriods.CountAsync(), await db.PaymentRequests.CountAsync());
        await db.SaveChangesAsync(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.GetAsync("/api/v1/guardian/home"); response.EnsureSuccessStatusCode(); var raw = await response.Content.ReadAsStringAsync(); var home = JsonDocument.Parse(raw).RootElement;
        Assert.Equal("2026-10-15", home.GetProperty("trainingToday").GetString()); Assert.Equal("2026-09-28", home.GetProperty("today").GetString());
        var children = home.GetProperty("children").EnumerateArray().ToArray();
        Assert.DoesNotContain(children, c => c.GetProperty("id").GetGuid() == privateChild.Id);
        var child = Assert.Single(children, c => c.GetProperty("id").GetGuid() == first.Id);
        Assert.Equal(latest.Id, child.GetProperty("latestEvaluation").GetProperty("id").GetGuid()); Assert.Equal(JsonValueKind.Null, child.GetProperty("latestEvaluation").GetProperty("overallScore").ValueKind);
        Assert.Equal(footballReport.Id, child.GetProperty("power").GetProperty("evaluationId").GetGuid()); Assert.Equal(80, child.GetProperty("power").GetProperty("overallScore").GetDecimal());
        var sports = child.GetProperty("sports").EnumerateArray().ToArray();
        Assert.Equal(next.Id, Assert.Single(sports, x => x.GetProperty("id").GetGuid() == e1.Id).GetProperty("nextSession").GetProperty("id").GetGuid());
        Assert.Equal(swimNext.Id, Assert.Single(sports, x => x.GetProperty("id").GetGuid() == swim.Id).GetProperty("nextSession").GetProperty("id").GetGuid());
        var sibling = Assert.Single(children, c => c.GetProperty("id").GetGuid() == second.Id);
        Assert.Equal(siblingReport.Id, sibling.GetProperty("latestEvaluation").GetProperty("id").GetGuid());
        Assert.Equal(siblingNext.Id, sibling.GetProperty("sports")[0].GetProperty("nextSession").GetProperty("id").GetGuid());
        Assert.DoesNotContain("NOTE-NOT-FOR-HOME", raw); Assert.DoesNotContain(draft.Id.ToString(), raw); Assert.DoesNotContain(unlinkedReport.Id.ToString(), raw); Assert.DoesNotContain(ended.Id.ToString(), raw);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/guardian/evaluations/{unlinkedReport.Id}/report")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/guardian/evaluations/{draft.Id}/report")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/v1/guardian/evaluations/{Academy.Api.Slice5.Slice5DemoSeed.AcademyBEvaluationId}/report")).StatusCode);
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/evaluations/{latest.Id}/report");
        Assert.Equal(latest.Id, detail.GetProperty("id").GetGuid());
        Assert.Equal(before, (await db.Collections.CountAsync(), await db.SubscriptionPeriods.CountAsync(), await db.PaymentRequests.CountAsync()));
    }
}
