using Academy.Api.Auth;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Demo;
public static class AssignedFollowUpDemoSeed
{
    public const string Email = "followup.football@example.test";
    public const string Phone = "01099900041";
    public static Guid MembershipId => FootballDemoSeed.Id("followup-delegate/member");
    public static async Task Seed(FoundationDbContext db, UserManager<ApplicationUser> users, IHostEnvironment env, DemoOptions options)
    {
        var a = FootballDemoSeed.AcademyId;
        if (!env.IsEnvironment("Demo") || !options.SeedEnabled || options.SeedProfile != "Football" || !await db.Academies.AnyAsync(x => x.Id == a && x.Slug == FootballDemoSeed.Slug && x.EnglishName == FootballDemoSeed.Marker)) throw new InvalidOperationException("مسؤول العرض الاصطناعي يتطلب Football Demo معلوم الهوية.");
        await using var tx = await db.Database.BeginTransactionAsync(); await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"football-followup-delegate-seed"},0))");
        var phone = EgyptPhoneNormalizer.Normalize(Phone)!;
        var id = FootballDemoSeed.Id("followup-delegate/user"); var user = await users.FindByIdAsync(id.ToString());
        if (user == null)
        {
            if (await users.FindByEmailAsync(Email) != null || await db.Users.AnyAsync(u => u.PhoneNumber == phone)) throw new InvalidOperationException("هوية عرض موجودة مختلفة؛ لن تُستبدل.");
            var now = DateTimeOffset.Parse("2026-09-28T06:00:00Z");
            user = new() { Id = id, UserName = Email, Email = Email, PhoneNumber = phone, DisplayName = "منسق المتابعات — عرض اصطناعي", IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now };
            if (!(await users.CreateAsync(user, options.StaffPassword)).Succeeded) throw new InvalidOperationException("تعذر إنشاء هوية العرض الاصطناعية.");
        }
        if (user.Email != Email || user.PhoneNumber != phone) throw new InvalidOperationException("هوية عرض مختلفة؛ لن تُستبدل.");
        var existing = await db.AcademyMemberships.SingleOrDefaultAsync(x => x.Id == MembershipId);
        if (existing != null) { if (existing.AcademyId != a || existing.UserId != user.Id) throw new InvalidOperationException("عضوية عرض مختلفة؛ لن تُستبدل."); return; }
        var at = DateTimeOffset.Parse("2026-09-28T06:00:00Z");
        db.AcademyMemberships.Add(new() { Id = MembershipId, AcademyId = a, UserId = user.Id, Role = AcademyRole.AcademyAdmin, AllBranches = false, AllSports = false, UseActionGrants = true, IsActive = true, CreatedAtUtc = at, UpdatedAtUtc = at });
        db.MembershipBranchAccesses.Add(new() { Id = FootballDemoSeed.Id("followup-delegate/branch"), AcademyId = a, MembershipId = MembershipId, BranchId = FootballDemoSeed.Id("branch/1"), CreatedAtUtc = at, UpdatedAtUtc = at });
        db.MembershipSportAccesses.Add(new() { Id = FootballDemoSeed.Id("followup-delegate/sport"), AcademyId = a, MembershipId = MembershipId, SportId = FootballDemoSeed.Id("sport"), CreatedAtUtc = at, UpdatedAtUtc = at });
        db.StaffActionGrants.Add(new() { Id = FootballDemoSeed.Id("followup-delegate/grant"), AcademyId = a, MembershipId = MembershipId, Module = "followups", Read = true, Edit = true, CreatedAtUtc = at, UpdatedAtUtc = at });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
