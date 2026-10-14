using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

// Explicit opt-in LegacyRegression fixture; never resets another account or scope.
public static class SportScopeDemoSeed
{
    public static readonly Guid UserId = Guid.Parse("a3000000-0000-0000-0000-000000000301");
    public static readonly Guid MembershipId = Guid.Parse("a3000000-0000-0000-0000-000000000302");
    public const string Email = "football.operator.v3@example.test";
    public static readonly Guid TemplateId = Guid.Parse("a3000000-0000-0000-0000-000000000350");
    public static async Task Seed(FoundationDbContext db, UserManager<ApplicationUser> users, IHostEnvironment env, DemoOptions options)
    {
        if (!env.IsEnvironment("Demo") || !options.SeedEnabled || options.SeedProfile != "LegacyRegression")
            throw new InvalidOperationException("هذا السيناريو لبيئة Demo الاصطناعية المعزولة فقط.");
        var academy = DemoSeed.NogoomAcademyId;
        if (!await db.Academies.AnyAsync(a => a.Id == academy && a.Slug == "nogoom-demo")
            || !await db.Branches.AnyAsync(b => b.Id == Slice2DemoSeed.CityBranchId && b.AcademyId == academy)
            || !await db.Sports.AnyAsync(s => s.Id == Slice2DemoSeed.FootballId && s.AcademyId == academy))
            throw new InvalidOperationException("المراجع الاصطناعية المعروفة غير موجودة؛ لا تنسخ صلاحيات إلى أكاديمية أخرى.");
        await using var tx = await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{academy}:{MembershipId}"}, 0))");
        var user = await db.Users.SingleOrDefaultAsync(u => u.Id == UserId);
        if (user is not null && (user.Email != Email || user.UserName != Email)) throw new InvalidOperationException("هوية محجوزة مستخدمة ببيانات أخرى؛ لم تتغير.");
        var now = DateTimeOffset.Parse("2026-09-28T06:00:00Z");
        if (user is null)
        {
            user = new() { Id = UserId, DisplayName = "إداري كرة القدم الاصطناعي", Email = Email, UserName = Email, CreatedAtUtc = now, UpdatedAtUtc = now };
            var created = await users.CreateAsync(user, options.StaffPassword);
            if (!created.Succeeded) throw new InvalidOperationException("تعذر إنشاء حساب العرض دون تغيير حساب قائم.");
        }
        var template = await db.StaffRoleTemplates.SingleOrDefaultAsync(r => r.Id == TemplateId);
        var marker = Guid.Parse("a3000000-0000-0000-0000-000000000351");
        if (template is not null && (template.AcademyId != academy || !await db.StaffRoleTemplateAudits.AnyAsync(a => a.Id == marker && a.TemplateId == TemplateId && a.AcademyId == academy)))
            throw new InvalidOperationException("قالب محجوز مستخدم دون علامة العرض المعروفة؛ لم يتغير.");
        if (template is null)
        {
            var name = "منسق متابعة كرة القدم — نموذج اصطناعي";
            template = new() { Id = TemplateId, AcademyId = academy, Name = name, NormalizedName = name, Description = "نقطة بداية للقراءة؛ اختر الحساب والفرع والرياضة ثم اعتمدها صراحة.", ActionsJson = JsonSerializer.Serialize(StaffAccessTemplates.All.Single(t => t.Key == "read-only").Actions, StaffRoleTemplateEndpoints.Json), CreatedAtUtc = now, UpdatedAtUtc = now };
            db.StaffRoleTemplates.Add(template);
            var seedActor = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync();
            db.StaffRoleTemplateAudits.Add(new() { Id = marker, AcademyId = academy, TemplateId = TemplateId, ActorUserId = seedActor, Before = "{}", After = JsonSerializer.Serialize(new { source = "synthetic-v3-sport-template", template = StaffRoleTemplateEndpoints.View(template) }, StaffRoleTemplateEndpoints.Json), CreatedAtUtc = now, UpdatedAtUtc = now });
        }
        var existing = await db.AcademyMemberships.SingleOrDefaultAsync(m => m.Id == MembershipId);
        if (existing is not null)
        {
            if (existing.AcademyId != academy || existing.UserId != UserId || existing.Role != AcademyRole.AcademyAdmin) throw new InvalidOperationException("عضوية محجوزة مستخدمة بسياق آخر؛ لم تتغير.");
            await db.SaveChangesAsync(); await tx.CommitAsync();
            return; // Preserve owner edits, revocations and disabled accounts on every restart.
        }
        db.AcademyMemberships.Add(new() { Id = MembershipId, AcademyId = academy, UserId = UserId, Role = AcademyRole.AcademyAdmin, JobTitle = "تشغيل كرة القدم — عرض اصطناعي", AllBranches = false, AllSports = false, UseActionGrants = true, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.MembershipBranchAccesses.Add(new() { Id = Guid.Parse("a3000000-0000-0000-0000-000000000303"), AcademyId = academy, MembershipId = MembershipId, BranchId = Slice2DemoSeed.CityBranchId, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.MembershipSportAccesses.Add(new() { Id = Guid.Parse("a3000000-0000-0000-0000-000000000304"), AcademyId = academy, MembershipId = MembershipId, SportId = Slice2DemoSeed.FootballId, CreatedAtUtc = now, UpdatedAtUtc = now });
        var actions = StaffAccessTemplates.All.Single(t => t.Key == "branch-operator").Actions;
        for (var i = 0; i < actions.Length; i++)
        {
            var a = actions[i]; db.StaffActionGrants.Add(new() { Id = Guid.Parse($"a3000000-0000-0000-0000-{(310 + i):D12}"), AcademyId = academy, MembershipId = MembershipId, Module = a.Module, Read = a.Read, Add = a.Add, Edit = a.Edit, Delete = a.Delete, CreatedAtUtc = now, UpdatedAtUtc = now });
        }
        var actor = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync();
        db.StaffAccessAudits.Add(new() { Id = Guid.Parse("a3000000-0000-0000-0000-000000000305"), AcademyId = academy, MembershipId = MembershipId, ActorUserId = actor, Before = "{}", After = JsonSerializer.Serialize(new { source = "synthetic-v3-sport-scope", AllBranches = false, Branches = new[] { Slice2DemoSeed.CityBranchId }, AllSports = false, Sports = new[] { Slice2DemoSeed.FootballId }, Actions = actions }), CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
