using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class AccessContextDemoSeed
{
    public static readonly Guid ContextId = Guid.Parse("a3000000-0000-0000-0000-000000000401");
    public static async Task Seed(FoundationDbContext db, IHostEnvironment env, DemoOptions options)
    {
        if (!env.IsEnvironment("Demo") || !options.SeedEnabled || options.SeedProfile != "LegacyRegression") throw new InvalidOperationException("سياق العرض لبيئة Demo الاصطناعية فقط.");
        var academy = DemoSeed.NogoomAcademyId; var primary = SportScopeDemoSeed.MembershipId;
        await using var tx = await db.Database.BeginTransactionAsync(); await StaffAccessContextEndpoints.Lock(db, academy, primary);
        if (!await db.AcademyMemberships.AnyAsync(m => m.Id == primary && m.UserId == SportScopeDemoSeed.UserId && m.AcademyId == academy && m.Role == AcademyRole.AcademyAdmin && m.PrimaryMembershipId == null)
            || !await db.Academies.AnyAsync(a => a.Id == academy && a.Slug == "nogoom-demo")
            || !await db.Branches.AnyAsync(b => b.Id == Slice2DemoSeed.FifthBranchId && b.AcademyId == academy)
            || !await db.Sports.AnyAsync(s => s.Id == Slice2DemoSeed.SwimmingId && s.AcademyId == academy)) throw new InvalidOperationException("مراجع العرض الاصطناعية المطلوبة غير موجودة؛ لا نسخ لسياق عميل.");
        var marker = Guid.Parse("a3000000-0000-0000-0000-000000000404");
        var existing = await db.AcademyMemberships.SingleOrDefaultAsync(m => m.Id == ContextId);
        if (existing is not null)
        {
            if (existing.PrimaryMembershipId != primary || existing.UserId != SportScopeDemoSeed.UserId || existing.AcademyId != academy || !await db.StaffAccessAudits.AnyAsync(a => a.Id == marker && a.MembershipId == ContextId && a.AcademyId == academy)) throw new InvalidOperationException("هوية السياق مستخدمة دون علامة العرض المعروفة؛ لم تتغير.");
            return; // Never reactivate or overwrite the owner's reviewed matrix.
        }
        var now = DateTimeOffset.Parse("2026-09-28T06:00:00Z"); var name = "متابعة السباحة — التجمع — عرض اصطناعي";
        db.AcademyMemberships.Add(new() { Id = ContextId, AcademyId = academy, UserId = SportScopeDemoSeed.UserId, PrimaryMembershipId = primary, Role = AcademyRole.AcademyAdmin, AccessContextName = name, NormalizedContextName = name, UseActionGrants = true, AllBranches = false, AllSports = false, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.MembershipBranchAccesses.Add(new() { Id = Guid.Parse("a3000000-0000-0000-0000-000000000402"), AcademyId = academy, MembershipId = ContextId, BranchId = Slice2DemoSeed.FifthBranchId, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.MembershipSportAccesses.Add(new() { Id = Guid.Parse("a3000000-0000-0000-0000-000000000403"), AcademyId = academy, MembershipId = ContextId, SportId = Slice2DemoSeed.SwimmingId, CreatedAtUtc = now, UpdatedAtUtc = now });
        var actions = StaffAccessTemplates.All.Single(t => t.Key == "read-only").Actions;
        foreach (var (action, index) in actions.Select((a, i) => (a, i))) db.StaffActionGrants.Add(new() { Id = Guid.Parse($"a3000000-0000-0000-0000-{(410 + index):D12}"), AcademyId = academy, MembershipId = ContextId, Module = action.Module, Read = action.Read, CreatedAtUtc = now, UpdatedAtUtc = now });
        db.StaffAccessAudits.Add(new() { Id = marker, AcademyId = academy, MembershipId = ContextId, ActorUserId = await db.Users.Where(u => u.Email == DemoSeed.OwnerEmail).Select(u => u.Id).SingleAsync(), Before = "{}", After = JsonSerializer.Serialize(new { source = "synthetic-v3-access-context", name, branch = Slice2DemoSeed.FifthBranchId, sport = Slice2DemoSeed.SwimmingId, actions }), CreatedAtUtc = now, UpdatedAtUtc = now });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
