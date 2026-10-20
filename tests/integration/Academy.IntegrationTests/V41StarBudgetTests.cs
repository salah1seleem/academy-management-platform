using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Mobile;
using Academy.Api.Slice4;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    [Fact] public async Task V41_stars_have_per_team_atomic_budget_idempotency_scope_and_immutable_history()
    {
        using var coach=await Mobile(AcademyRole.Coach);using var other=await Staff(DemoSeed.FutureOwnerEmail);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var academy=DemoSeed.NogoomAcademyId;
        var hash=MobileAuthenticationHandler.Hash(coach.DefaultRequestHeaders.Authorization!.Parameter!);
        var member=await db.MobileSessions.Where(s=>s.AccessTokenHash==hash).Select(s=>s.MembershipId!.Value).SingleAsync();
        var enrollment=await db.SportEnrollments.Include(e=>e.TrainingGroup).FirstAsync(e=>e.AcademyId==academy&&e.IsActive&&e.TrainingGroup!=null&&e.Player.IsActive);
        var tournament=new AcademyTournament{Id=Guid.NewGuid(),AcademyId=academy,Name="بطولة ميزانية النجوم الاصطناعية",SportId=enrollment.SportId,Fee=0,Currency="EGP"};db.AcademyTournaments.Add(tournament);
        var teams=new List<MatchTeam>();
        for(var n=0;n<2;n++){
            var team=new MatchTeam{Id=Guid.NewGuid(),AcademyId=academy,Name="فريق نجوم اصطناعي "+n,SportId=enrollment.SportId,BranchId=enrollment.BranchId,AgeCategoryId=enrollment.TrainingGroup!.AgeCategoryId,CoachMembershipId=member};teams.Add(team);db.MatchTeams.Add(team);
            var match=new AcademyMatch{Id=Guid.NewGuid(),AcademyId=academy,TeamId=team.Id,TournamentId=tournament.Id,Opponent="فريق اصطناعي",MatchDate=new(2026,9,28),StartTime=new(17,0),Venue="ملعب اختبار",PublishedAtUtc=DateTimeOffset.UtcNow};db.AcademyMatches.Add(match);
            db.MatchTeamPlayers.Add(new(){Id=Guid.NewGuid(),AcademyId=academy,TeamId=team.Id,EnrollmentId=enrollment.Id});
            db.MatchParticipants.Add(new(){Id=Guid.NewGuid(),AcademyId=academy,MatchId=match.Id,PlayerId=enrollment.PlayerId});
        }
        await db.SaveChangesAsync();
        var request=Guid.NewGuid();var body=new{teamId=teams[0].Id,playerId=enrollment.PlayerId,requestId=request};
        (await Send(coach,"/api/v1/coach/monthly-stars",body)).EnsureSuccessStatusCode();
        (await Send(coach,"/api/v1/coach/monthly-stars",body)).EnsureSuccessStatusCode();
        Assert.Single(await db.MonthlyPlayerStars.Where(s=>s.TeamId==teams[0].Id).ToArrayAsync());
        Assert.Equal(HttpStatusCode.Conflict,(await Send(coach,"/api/v1/coach/monthly-stars",new{teamId=teams[1].Id,playerId=enrollment.PlayerId,requestId=request})).StatusCode);
        var responses=await Task.WhenAll(Enumerable.Range(0,5).Select(_=>Send(coach,"/api/v1/coach/monthly-stars",new{teamId=teams[0].Id,playerId=enrollment.PlayerId,requestId=Guid.NewGuid()})));
        Assert.Equal(2,responses.Count(r=>r.IsSuccessStatusCode));Assert.Equal(3,responses.Count(r=>r.StatusCode==HttpStatusCode.Conflict));
        var stars=await db.MonthlyPlayerStars.AsNoTracking().Where(s=>s.TeamId==teams[0].Id).OrderBy(s=>s.Slot).ToArrayAsync();Assert.Equal(new[]{1,2,3},stars.Select(s=>s.Slot));
        var today=await AttendanceCalendar.TodayAsync(academy,db,scope.ServiceProvider.GetRequiredService<TimeProvider>());Assert.All(stars,s=>Assert.Equal(new DateOnly(today.Year,today.Month,1),s.Month));
        (await Send(coach,"/api/v1/coach/monthly-stars",new{teamId=teams[1].Id,playerId=enrollment.PlayerId,requestId=Guid.NewGuid()})).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.NotFound,(await Send(coach,"/api/v1/coach/monthly-stars",new{teamId=Guid.NewGuid(),playerId=enrollment.PlayerId,requestId=Guid.NewGuid()})).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,(await Send(other,"/api/v1/coach/monthly-stars",body)).StatusCode);
        Assert.DoesNotContain((await other.GetFromJsonAsync<JsonElement>("/api/v1/reports/featured-players")).GetProperty("items").EnumerateArray(),p=>p.GetProperty("id").GetGuid()==enrollment.PlayerId);
        var id=stars[0].Id;await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"MonthlyPlayerStars\" SET \"Slot\"=2 WHERE \"Id\"={id}"));
    }
}
