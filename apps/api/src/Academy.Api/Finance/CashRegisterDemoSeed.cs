using Academy.Api.Auth;
using Academy.Api.Slice2;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Finance;

// Caller is explicitly enabled Demo/LegacyRegression only. No customer reset,
// entitlement activation, real collection or recreation of an edited fixture.
public static class CashRegisterDemoSeed
{
    public static readonly Guid RegisterId = Guid.Parse("84000001-0000-0000-0000-000000000001");
    public static readonly Guid SessionId = Guid.Parse("84000002-0000-0000-0000-000000000001");
    public static async Task Seed(FoundationDbContext db)
    {
        await using var tx=await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"v2-demo-cash-register"},0))");
        if(await db.CashRegisters.AnyAsync(r=>r.Id==RegisterId)||await db.CashSessions.AnyAsync(s=>s.Id==SessionId))return;
        var academy=DemoSeed.NogoomAcademyId;var member=await db.AcademyMemberships.SingleAsync(m=>m.AcademyId==academy&&m.User.Email==DemoSeed.OwnerEmail);
        var branch=Slice2DemoSeed.CityBranchId;if(!await db.Branches.AnyAsync(b=>b.Id==branch&&b.AcademyId==academy&&b.IsActive))return;
        var currency=await db.Academies.Where(a=>a.Id==academy).Select(a=>a.DefaultCurrency).SingleAsync();
        var at=new DateTimeOffset(2026,9,28,6,0,0,TimeSpan.Zero);
        db.CashRegisters.Add(new(){Id=RegisterId,AcademyId=academy,BranchId=branch,Name="صندوق المطابقة — مثال اصطناعي",Currency=currency,CreatedByUserId=member.UserId,RequestKey="v2-demo-cash-register",RequestHash="DEMO-CASH-V1",CreatedAtUtc=at,UpdatedAtUtc=at});
        db.CashierAssignments.Add(new(){Id=Guid.Parse("84000003-0000-0000-0000-000000000001"),AcademyId=academy,BranchId=branch,RegisterId=RegisterId,MembershipId=member.Id,CreatedAtUtc=at,UpdatedAtUtc=at});
        db.CashSessions.Add(new(){Id=SessionId,AcademyId=academy,BranchId=branch,RegisterId=RegisterId,OpenedByMembershipId=member.Id,OpeningAmount=200,OpenedAtUtc=at,OpenKey="v2-demo-cash-open",OpenHash="DEMO-CASH-V1",ClosedAtUtc=at.AddHours(8),ClosedByUserId=member.UserId,CountedAmount=190,ExpectedAmount=200,ClosingReason="فرق عد اصطناعي للمراجعة؛ لا يولد مصروفًا أو إيرادًا",CloseKey="v2-demo-cash-close",CloseHash="DEMO-CASH-V1",CreatedAtUtc=at,UpdatedAtUtc=at.AddHours(8)});
        await db.SaveChangesAsync();await tx.CommitAsync();
    }
}
