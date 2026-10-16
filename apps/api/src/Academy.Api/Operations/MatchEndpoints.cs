using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class MatchEndpoints
{
    private static Guid Actor(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    public static void MapMatches(this WebApplication app)
    {
        var manage = app.MapGroup("/api/v1/manage/matches").RequireAuthorization(AcademyPermissions.StructureManage);
        manage.MapGet("/options", Options);
        manage.MapGet("/", List);
        manage.MapGet("/{id:guid}", async(Guid id,CurrentTenant tenant,FoundationDbContext db,HttpResponse response)=>{response.Headers.CacheControl="no-store";var t=(await tenant.ResolveAsync())!;var row=await Rows(Query(t.AcademyId,db).Where(x=>x.Id==id),db,t.AcademyId).SingleOrDefaultAsync();return row is null?Results.NotFound():Results.Ok(row);});
        manage.MapGet("/{id:guid}/copy-preview",MatchDrafts.Preview);
        manage.MapPost("/{id:guid}/copy",MatchDrafts.Copy).AddEndpointFilter<CsrfFilter>();
        manage.MapGet("/teams/{id:guid}/roster",MatchDrafts.RosterPreview);
        manage.MapPost("/tournaments", Tournament).AddEndpointFilter<CsrfFilter>();
        manage.MapPut("/tournaments/{id:guid}", Tournament).AddEndpointFilter<CsrfFilter>();
        manage.MapPost("/teams", Team).AddEndpointFilter<CsrfFilter>();
        manage.MapPut("/teams/{id:guid}", Team).AddEndpointFilter<CsrfFilter>();
        manage.MapPost("/", Save).AddEndpointFilter<CsrfFilter>();
        manage.MapPut("/{id:guid}", Save).AddEndpointFilter<CsrfFilter>();
        app.MapGet("/api/v1/my-matches", Mine).RequireAuthorization(AcademyPermissions.TenantAccess);
    }
    private static async Task<IResult> Options(CurrentTenant tenant, FoundationDbContext db, TimeProvider time)
    {
        var t = (await tenant.ResolveAsync())!; var a = t.AcademyId;
        var groupDate = await Academy.Api.Slice4.AttendanceCalendar.TodayAsync(a, db, time);
        return Results.Ok(new {
            canCopy=t.Role==AcademyRole.AcademyOwner||t.Role==AcademyRole.AcademyAdmin&&await db.AcademyMemberships.Where(m=>m.Id==t.MembershipId&&m.IsActive).Select(m=>m.UseActionGrants?db.StaffActionGrants.Any(g=>g.MembershipId==m.Id&&g.IsActive&&g.Module=="matches"&&g.Read&&g.Add):db.StaffGrants.Any(g=>g.MembershipId==m.Id&&g.IsActive&&g.Permission==AcademyPermissions.StructureManage&&g.Allowed)).SingleAsync(),
            tournaments = await db.AcademyTournaments.Where(x => x.AcademyId == a).OrderBy(x => x.Name).ToListAsync(),
            teams = await db.MatchTeams.Where(x => x.AcademyId == a).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name, x.SportId, x.BranchId, x.AgeCategoryId, x.CoachMembershipId, x.IsActive, enrollmentIds = db.MatchTeamPlayers.Where(p => p.AcademyId == a && p.TeamId == x.Id && p.IsActive).Select(p => p.EnrollmentId).ToList() }).ToListAsync(),
            players = await db.SportEnrollments.Where(x => x.AcademyId == a && x.IsActive && x.Player.IsActive && x.Sport.IsActive && x.Branch.IsActive).OrderBy(x => x.Player.ArabicName).Take(1000).Select(x => new { x.Id, playerId = x.PlayerId, name = x.Player.ArabicName, birthYear = x.Player.DateOfBirth.Year, x.SportId, x.BranchId, trainingGroupId = FoundationDbContext.AssignedGroupAt(x.AcademyId, x.Id, groupDate) }).ToListAsync(),
            coaches = await db.AcademyMemberships.Where(x => x.AcademyId == a && x.IsActive && x.Role == AcademyRole.Coach && x.User.IsActive).Select(x => new { x.Id, name = x.User.DisplayName }).ToListAsync()
        });
    }
    private static IQueryable<AcademyMatch> Query(Guid academy, FoundationDbContext db) => db.AcademyMatches.AsNoTracking().Where(x => x.AcademyId == academy);
    private static async Task<IResult> List(Guid? tournamentId, Guid? teamId, Guid? playerId, Guid? coachMembershipId, DateOnly? from, DateOnly? to, CurrentTenant tenant, FoundationDbContext db, HttpResponse response)
    {
        response.Headers.CacheControl="no-store";
        var t = (await tenant.ResolveAsync())!; var q = Query(t.AcademyId, db);
        if(playerId.HasValue&&!await db.Players.AnyAsync(p=>p.Id==playerId&&p.AcademyId==t.AcademyId))return Results.NotFound();
        if(coachMembershipId.HasValue&&!await db.AcademyMemberships.AnyAsync(m=>m.Id==coachMembershipId&&m.AcademyId==t.AcademyId&&m.Role==AcademyRole.Coach))return Results.NotFound();
        if(playerId.HasValue)q=q.Where(x=>db.MatchParticipants.Any(p=>p.MatchId==x.Id&&p.PlayerId==playerId&&p.IsActive));
        if(coachMembershipId.HasValue)q=q.Where(x=>db.MatchTeams.Any(team=>team.Id==x.TeamId&&team.CoachMembershipId==coachMembershipId));
        if (from.HasValue && to.HasValue && from > to) return Results.UnprocessableEntity();
        q = q.Where(x => (!tournamentId.HasValue || x.TournamentId == tournamentId) && (!teamId.HasValue || x.TeamId == teamId) && (!from.HasValue || x.MatchDate >= from) && (!to.HasValue || x.MatchDate <= to));
        return Results.Ok(await Rows(q.OrderByDescending(x => x.MatchDate).Take(200), db, t.AcademyId).ToListAsync());
    }
    private static IQueryable<object> Rows(IQueryable<AcademyMatch> q, FoundationDbContext db, Guid a) => q.Select(x => (object)new {
        x.Id, x.TournamentId, x.TeamId, x.Opponent, x.MatchDate, x.StartTime, x.Venue, x.Notes, x.OurScore, x.OpponentScore, x.PublishedAtUtc, x.IsActive,x.CopiedFromMatchId,
        status=!x.IsActive?"Cancelled":x.PublishedAtUtc==null?"Draft":x.OurScore!=null?"Completed":"Published",
        coachAssignmentSource="CurrentTeam",
        tournament = db.AcademyTournaments.Where(t => t.AcademyId == a && t.Id == x.TournamentId).Select(t => t.Name).Single(),
        team = db.MatchTeams.Where(t => t.AcademyId == a && t.Id == x.TeamId).Select(t => t.Name).Single(),
        players = db.MatchParticipants.Where(p => p.AcademyId == a && p.MatchId == x.Id && p.IsActive).Select(p => new { p.PlayerId, p.IsBench }).ToList()
    });
    private static async Task<IResult> Mine(Guid? playerId, CurrentTenant tenant, ClaimsPrincipal user, FoundationDbContext db, ISubscriptionClock clock, HttpResponse response)
    {
        response.Headers.CacheControl="no-store";
        var t = (await tenant.ResolveAsync())!; var u = Actor(user); var q = Query(t.AcademyId, db).Where(x => x.PublishedAtUtc != null);
        if(playerId.HasValue)q=q.Where(x=>db.MatchParticipants.Any(p=>p.MatchId==x.Id&&p.PlayerId==playerId&&p.IsActive));
        if (t.Role == AcademyRole.Guardian)
            q = q.Where(x => db.MatchParticipants.Any(p => p.AcademyId == t.AcademyId && p.MatchId == x.Id && p.IsActive && (!playerId.HasValue || p.PlayerId == playerId) && db.GuardianPlayerLinks.Any(l => l.AcademyId == t.AcademyId && l.PlayerId == p.PlayerId && l.Guardian.UserId == u && l.IsActive && l.Guardian.IsActive && l.Player.IsActive)));
        else if (t.Role == AcademyRole.Coach)
            q = q.Where(x => db.MatchTeams.Any(team => team.AcademyId == t.AcademyId && team.Id == x.TeamId && team.IsActive && team.CoachMembershipId == t.MembershipId));
        else if (t.Role != AcademyRole.AcademyOwner) return Results.Forbid();
        // No roster names/IDs in the role feed: only the caller's linked child IDs.
        return Results.Ok(await q.OrderBy(x => x.MatchDate >= clock.Today ? 0 : 1).ThenBy(x => x.MatchDate >= clock.Today ? x.MatchDate : DateOnly.MaxValue).ThenByDescending(x => x.MatchDate).ThenBy(x => x.StartTime).Take(100).Select(x => new {
            x.Id, x.Opponent, x.MatchDate, x.StartTime, x.Venue, x.Notes, x.OurScore, x.OpponentScore,
            team = db.MatchTeams.Where(team => team.AcademyId == t.AcademyId && team.Id == x.TeamId).Select(team => team.Name).Single(),
            tournament = db.AcademyTournaments.Where(c => c.AcademyId == t.AcademyId && c.Id == x.TournamentId).Select(c => c.Name).Single(),
            status=!x.IsActive?"Cancelled":x.OurScore!=null?"Completed":"Published",
            participation=playerId.HasValue?db.MatchParticipants.Where(p=>p.MatchId==x.Id&&p.PlayerId==playerId&&p.IsActive).Select(p=>p.IsBench?"Bench":"Player").FirstOrDefault():null,
            coachAssignmentSource=t.Role==AcademyRole.Coach?"CurrentTeam":null,
            isUpcoming = x.IsActive && x.MatchDate >= clock.Today && x.OurScore == null,
            children = db.MatchParticipants.Where(p => p.AcademyId == t.AcademyId && p.MatchId == x.Id && p.IsActive && db.GuardianPlayerLinks.Any(l => l.AcademyId == t.AcademyId && l.PlayerId == p.PlayerId && l.Guardian.UserId == u && l.IsActive && l.Guardian.IsActive && l.Player.IsActive)).Select(p => p.PlayerId).ToList()
        }).ToListAsync());
    }
    private static async Task<IResult> Tournament(Guid? id, TournamentWrite body, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!;
        if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 120 || body.Fee < 0 || body.Fee > 9999999999999999.99m || decimal.Round(body.Fee, 2) != body.Fee || body.Currency != await db.Academies.Where(x => x.Id == t.AcademyId).Select(x => x.DefaultCurrency).SingleAsync() || !await db.Sports.AnyAsync(x => x.AcademyId == t.AcademyId && x.Id == body.SportId && x.IsActive)) return Results.UnprocessableEntity(new { message = "أدخل اسم البطولة والرياضة والرسم الصحيح بعملة الأكاديمية." });
        var row = id.HasValue ? await db.AcademyTournaments.SingleOrDefaultAsync(x => x.AcademyId == t.AcademyId && x.Id == id) : new AcademyTournament { Id = Guid.NewGuid(), AcademyId = t.AcademyId, Name = body.Name.Trim(), Currency = body.Currency, CreatedAtUtc = clock.UtcNow };
        if (row == null) return Results.NotFound();
        if (!id.HasValue) db.AcademyTournaments.Add(row);
        row.Name = body.Name.Trim(); row.SportId = body.SportId; row.Fee = body.Fee; row.Currency = body.Currency; row.IsActive = body.IsActive; row.UpdatedAtUtc = clock.UtcNow; await db.SaveChangesAsync(); return Results.Ok(new { row.Id });
    }
    private static async Task<IResult> Team(Guid? id, TeamWrite body, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var a = t.AcademyId;
        var category = await db.AgeCategories.SingleOrDefaultAsync(x => x.AcademyId == a && x.Id == body.AgeCategoryId && x.IsActive);
        if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 120 || body.EnrollmentIds == null || body.EnrollmentIds.Length is < 1 or > 200 || body.EnrollmentIds.Distinct().Count() != body.EnrollmentIds.Length || category == null || !await db.Sports.AnyAsync(x => x.AcademyId == a && x.Id == body.SportId && x.IsActive) || !await db.Branches.AnyAsync(x => x.AcademyId == a && x.Id == body.BranchId && x.IsActive) || !await db.AcademyMemberships.AnyAsync(x => x.AcademyId == a && x.Id == body.CoachMembershipId && x.Role == AcademyRole.Coach && x.IsActive && x.User.IsActive)) return Results.UnprocessableEntity(new { message = "اختر اسم الفريق والرياضة والفرع والفئة والمدرب وقائمة اللاعبين." });
        var valid = await db.SportEnrollments.CountAsync(x => x.AcademyId == a && body.EnrollmentIds.Contains(x.Id) && x.SportId == body.SportId && x.IsActive && x.Player.IsActive && x.Player.DateOfBirth.Year >= category.MinimumBirthYear && x.Player.DateOfBirth.Year <= category.MaximumBirthYear);
        if (valid != body.EnrollmentIds.Length) return Results.UnprocessableEntity(new { message = "لاعبو المنتخب من نفس الرياضة والفئة العمرية داخل الأكاديمية." });
        await using var tx = await db.Database.BeginTransactionAsync();
        if(id.HasValue)await MatchDrafts.TeamLock(db,a,id.Value);
        var row = id.HasValue ? await db.MatchTeams.SingleOrDefaultAsync(x => x.AcademyId == a && x.Id == id) : new MatchTeam { Id = Guid.NewGuid(), AcademyId = a, Name = body.Name.Trim(), CreatedAtUtc = clock.UtcNow };
        if (row == null) return Results.NotFound();
        if (!id.HasValue) db.MatchTeams.Add(row);
        else if (await db.AcademyMatches.AnyAsync(x => x.AcademyId == a && x.TeamId == id) && (row.SportId != body.SportId || row.AgeCategoryId != body.AgeCategoryId)) return Results.Conflict(new { message = "أنشئ فريقًا جديدًا لتغيير رياضة أو فئة فريق له مباريات محفوظة." });
        row.Name = body.Name.Trim(); row.SportId = body.SportId; row.BranchId = body.BranchId; row.AgeCategoryId = body.AgeCategoryId; row.CoachMembershipId = body.CoachMembershipId; row.IsActive = body.IsActive; row.UpdatedAtUtc = clock.UtcNow;
        var existing = await db.MatchTeamPlayers.Where(x => x.AcademyId == a && x.TeamId == row.Id).ToListAsync();
        foreach (var p in existing) { p.IsActive = body.EnrollmentIds.Contains(p.EnrollmentId); p.UpdatedAtUtc = clock.UtcNow; }
        foreach (var e in body.EnrollmentIds.Where(e => !existing.Any(x => x.EnrollmentId == e))) db.MatchTeamPlayers.Add(new MatchTeamPlayer { Id = Guid.NewGuid(), AcademyId = a, TeamId = row.Id, EnrollmentId = e, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow });
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { row.Id });
    }
    private static async Task<IResult> Save(Guid? id, MatchWrite body, CurrentTenant tenant, FoundationDbContext db, ISubscriptionClock clock)
    {
        var t = (await tenant.ResolveAsync())!; var a = t.AcademyId;
        if (id.HasValue && !await db.AcademyMatches.AnyAsync(x => x.AcademyId == a && x.Id == id)) return Results.NotFound();
        var team = await db.MatchTeams.AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == a && x.Id == body.TeamId && x.IsActive);
        if (team == null || !await db.AcademyTournaments.AnyAsync(x => x.AcademyId == a && x.Id == body.TournamentId && x.SportId == team.SportId && x.IsActive) || string.IsNullOrWhiteSpace(body.Opponent) || body.Opponent.Length > 120 || string.IsNullOrWhiteSpace(body.Venue) || body.Venue.Length > 200 || body.Notes?.Length > 500 || body.MatchDate.Year is < 2000 or > 2100 || body.OurScore is < 0 or > 100 || body.OpponentScore is < 0 or > 100 || body.OurScore.HasValue != body.OpponentScore.HasValue || body.OurScore.HasValue && body.MatchDate > clock.Today || body.BenchPlayerIds?.Length > 200) return Results.UnprocessableEntity(new { message = "راجع البطولة والفريق والموعد والملعب والنتيجة (للمباريات الماضية فقط)." });
        await using var tx = await db.Database.BeginTransactionAsync();
        await MatchDrafts.TeamLock(db,a,body.TeamId);
        if (id.HasValue) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"match:{a}:{id}"}, 0))");
        team=await db.MatchTeams.AsNoTracking().SingleOrDefaultAsync(x=>x.AcademyId==a&&x.Id==body.TeamId&&x.IsActive);if(team is null)return Results.Conflict();
        var row = id.HasValue ? await db.AcademyMatches.SingleOrDefaultAsync(x => x.AcademyId == a && x.Id == id) : new AcademyMatch { Id = Guid.NewGuid(), AcademyId = a, Opponent = body.Opponent.Trim(), Venue = body.Venue.Trim(), CreatedAtUtc = clock.UtcNow };
        if (row == null) return Results.NotFound();
        if (row.PublishedAtUtc != null && (row.TeamId != body.TeamId || row.TournamentId != body.TournamentId)) return Results.Conflict(new { message = "الفريق والبطولة ثابتان بعد النشر؛ أنشئ مباراة مستقلة." });
        if (!id.HasValue) db.AcademyMatches.Add(row);
        var audience = await db.MatchParticipants.Where(x => x.AcademyId == a && x.MatchId == row.Id && x.IsActive).ToListAsync();
        if (row.PublishedAtUtc == null)
        {
            var reviewedRoster=await MatchDrafts.Roster(db,team);
            if(body.Publish&&(row.CopiedFromMatchId.HasValue||body.RosterQuoteHash is not null)&&body.RosterQuoteHash!=MatchDrafts.RosterHash(team,reviewedRoster))return Results.Problem(statusCode:409,title:"راجع قائمة المشاركين الحالية قبل نشر المباراة المنسوخة؛ قد تغير الفريق.");
            var roster = reviewedRoster.Select(p=>p.PlayerId).Distinct().ToList();
            if (body.Publish && roster.Count == 0 || body.BenchPlayerIds?.Any(p => !roster.Contains(p)) == true) return Results.UnprocessableEntity(new { message = "قائمة الفريق فارغة أو البدلاء ليسوا من الفريق." });
            if (body.Publish) foreach (var player in roster) { var p = new MatchParticipant { Id = Guid.NewGuid(), AcademyId = a, MatchId = row.Id, PlayerId = player, IsBench = body.BenchPlayerIds?.Contains(player) == true, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow }; audience.Add(p); db.MatchParticipants.Add(p); }
        }
        else { if (body.BenchPlayerIds?.Any(p => !audience.Any(x => x.PlayerId == p)) == true) return Results.UnprocessableEntity(); foreach (var p in audience) p.IsBench = body.BenchPlayerIds?.Contains(p.PlayerId) == true; }
        row.TeamId = body.TeamId; row.TournamentId = body.TournamentId; row.Opponent = body.Opponent.Trim(); row.MatchDate = body.MatchDate; row.StartTime = body.StartTime; row.Venue = body.Venue.Trim(); row.Notes = body.Notes?.Trim(); row.OurScore = body.OurScore; row.OpponentScore = body.OpponentScore; row.IsActive = body.IsActive; row.UpdatedAtUtc = clock.UtcNow;
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { row.Opponent, row.MatchDate, row.StartTime, row.Venue, row.Notes, row.OurScore, row.OpponentScore, row.IsActive, bench = audience.Where(x => x.IsBench).Select(x => x.PlayerId).Order().ToArray() }))));
        if ((body.Publish || row.PublishedAtUtc != null) && row.PublishedFingerprint != fingerprint)
        {
            row.PublishedAtUtc ??= clock.UtcNow; row.PublishedFingerprint = fingerprint;
            var players = audience.Select(x => x.PlayerId).ToArray();
            var users = await db.GuardianPlayerLinks.Where(l => l.AcademyId == a && players.Contains(l.PlayerId) && l.IsActive && l.Guardian.IsActive && l.Player.IsActive).Select(l => l.Guardian.UserId).Distinct().ToListAsync();
            var coachUser = await db.AcademyMemberships.Where(x => x.AcademyId == a && x.Id == team.CoachMembershipId && x.IsActive && x.User.IsActive).Select(x => (Guid?)x.UserId).SingleOrDefaultAsync(); if (coachUser.HasValue) users.Add(coachUser.Value);
            foreach (var u in users.Distinct()) db.AcademyNotices.Add(new AcademyNotice { Id = Guid.NewGuid(), AcademyId = a, UserId = u, Message = row.IsActive ? $"مباراة {team.Name} أمام {row.Opponent} يوم {row.MatchDate:yyyy-MM-dd} الساعة {row.StartTime:HH:mm} في {row.Venue}. راجع المواعيد داخل التطبيق." : $"تم إلغاء مباراة {team.Name} أمام {row.Opponent}. راجع إدارة الأكاديمية.", CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow });
        }
        await db.SaveChangesAsync(); await tx.CommitAsync(); return Results.Ok(new { row.Id });
    }
}
public sealed record TournamentWrite(string Name, Guid SportId, decimal Fee, string Currency, bool IsActive);
public sealed record TeamWrite(string Name, Guid SportId, Guid BranchId, Guid AgeCategoryId, Guid CoachMembershipId, Guid[] EnrollmentIds, bool IsActive);
public sealed record MatchWrite(Guid TournamentId, Guid TeamId, string Opponent, DateOnly MatchDate, TimeOnly StartTime, string Venue, string? Notes, int? OurScore, int? OpponentScore, Guid[]? BenchPlayerIds, bool Publish, bool IsActive,string? RosterQuoteHash=null);
