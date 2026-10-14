using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Demo;

// Explicit opt-in fixture. Never resets existing terms or grants permissions.
public static class CoachAgreementDemoSeed
{
    public static Guid AgreementId => FootballDemoSeed.Id("coach-agreement-demo/approved");
    public static Guid DraftId => FootballDemoSeed.Id("coach-agreement-demo/draft");
    public static Guid ApprovalId => FootballDemoSeed.Id("coach-agreement-demo/approval");
    public static async Task Seed(FoundationDbContext db, IHostEnvironment env, DemoOptions options)
    {
        if (!env.IsEnvironment("Demo") || !options.SeedEnabled || options.SeedProfile != "Football") throw new InvalidOperationException("اتفاقات العرض تتطلب ملف Football الاصطناعي وبيئة Demo صريحة.");
        var academy = FootballDemoSeed.AcademyId; var coach = FootballDemoSeed.Id("member/coach1");
        if (!await db.Academies.AnyAsync(a => a.Id == academy && a.Slug == FootballDemoSeed.Slug && a.EnglishName == FootballDemoSeed.Marker)) throw new InvalidOperationException("علامة أكاديمية العرض غير موجودة؛ لا تهيئة لبيانات مجهولة.");
        await using var tx = await db.Database.BeginTransactionAsync();
        var employee = await db.Employees.SingleAsync(e => e.AcademyId == academy && e.MembershipId == coach);
        await PayrollEndpoints.Lock(db, academy, employee.Id, new(2026,10,1));
        var existing = await db.CoachCompensationAgreements.SingleOrDefaultAsync(a => a.Id == AgreementId);
        if (existing is not null)
        {
            if (existing.AcademyId != academy || existing.CoachMembershipId != coach || existing.EmployeeId != employee.Id || !await db.CoachCompensationApprovals.AnyAsync(a => a.Id == ApprovalId && a.AgreementId == AgreementId)) throw new InvalidOperationException("مرجع العرض محجوز بسياق مختلف؛ لم يتغير.");
            return;
        }
        // A manually prepared agreement wins over this optional fixture. Never
        // inject a version 1 or an earlier rate into the owner's existing chain.
        if (await db.CoachCompensationAgreements.AnyAsync(a => a.AcademyId == academy && a.EmployeeId == employee.Id)) return;
        var owner = await db.AcademyMemberships.SingleAsync(m => m.AcademyId == academy && m.Role == AcademyRole.AcademyOwner && m.UserId == FootballDemoSeed.Id("user/owner"));
        var stamp = DateTimeOffset.Parse("2026-09-28T06:00:00Z"); var sport = FootballDemoSeed.Id("sport");
        foreach (var pair in new[] { (AgreementId, new DateOnly(2026,10,1), 40m), (DraftId, new DateOnly(2026,11,1), 30m) })
            db.CoachCompensationAgreements.Add(new() { Id = pair.Item1, AcademyId = academy, EmployeeId = employee.Id, CoachMembershipId = coach, BranchId = employee.BranchId, SportId = sport, EffectiveFrom = pair.Item2, BaseSalarySnapshot = employee.MonthlySalary, Currency = employee.Currency, VariableMethod = "Percent", VariableValue = pair.Item3, Eligibility = "DeliveredCollected", BaseCoverage = "GroupOnly", Note = "اتفاق اصطناعي لعرض النسخ المعتمدة والمسودة", CreatedByMembershipId = owner.Id, IdempotencyKey = pair.Item1.ToString(), CreatedAtUtc = stamp, UpdatedAtUtc = stamp });
        db.CoachCompensationApprovals.Add(new() { Id = ApprovalId, AcademyId = academy, AgreementId = AgreementId, EmployeeId = employee.Id, BranchId = employee.BranchId, SportId = sport, Revision = 1, ApprovedByMembershipId = owner.Id, ApprovedAtUtc = stamp, CreatedAtUtc = stamp, UpdatedAtUtc = stamp });
        await db.SaveChangesAsync(); await tx.CommitAsync();
    }
}
