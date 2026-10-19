using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Demo;
public static class V4AcceptanceSeed
{
    // Explicit opt-in and a separate database name: never seed the manual demo.
    public static async Task Seed(FoundationDbContext db,IHostEnvironment env,DemoOptions options,TimeProvider clock)
    {
        var academy=FootballDemoSeed.AcademyId;
        if(!env.IsEnvironment("Demo")||!options.SeedEnabled||options.SeedProfile!="Football"||!db.Database.GetDbConnection().Database.StartsWith("academy_v4_",StringComparison.Ordinal)
            ||!await db.Academies.AnyAsync(a=>a.Id==academy&&a.Slug==FootballDemoSeed.Slug&&a.EnglishName==FootballDemoSeed.Marker))throw new InvalidOperationException("بيانات قبولV4 تتطلب قاعدة academy_v4_ معزولة وFootball Demo معلوم الهوية.");
        await using var tx=await db.Database.BeginTransactionAsync();await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"v4-acceptance-seed"},0))");
        var owner=FootballDemoSeed.Id("member/owner");var today=await AttendanceCalendar.TodayAsync(academy,db,clock);var now=clock.GetUtcNow();
        async Task Add(string kind,Guid id,string name,Guid? branch,string? branchName,string note,int priority)
        {
            var task=FootballDemoSeed.Id("v4/followup/"+kind);
            if(await db.OwnerFollowUps.AnyAsync(f=>f.Id==task||f.SourceKind==kind&&f.SourceId==id&&f.State!="Closed"&&f.State!="Rejected"))return;
            var row=new OwnerFollowUp{Id=task,AcademyId=academy,SourceKind=kind,SourceId=id,SourceNameSnapshot=name,SourceBranchId=branch,SourceBranchSnapshot=branchName,ManualPriority=priority,Condition="RecordFollowUp",State="Assigned",AssignedToMembershipId=owner,CreatedByMembershipId=owner,CreatedOn=today,DueOn=today,Note=note,RequestId=FootballDemoSeed.Id("v4/followup/request/"+kind),RequestHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("V4 acceptance "+kind))),EvidenceJson=JsonSerializer.Serialize(new{sourceKind=kind,source=new{id,name},observedAtUtc=now,synthetic=true}),CreatedAtUtc=now,UpdatedAtUtc=now};
            db.OwnerFollowUps.Add(row);db.OwnerFollowUpChanges.Add(new(){Id=FootballDemoSeed.Id("v4/followup/history/"+kind),AcademyId=academy,FollowUpId=task,ActorMembershipId=owner,RequestId=row.RequestId,RequestHash=row.RequestHash,BeforeJson="{}",AfterJson=JsonSerializer.Serialize(new{row.State,row.AssignedToMembershipId,row.DueOn,row.Note,row.ResolutionReason}),CreatedAtUtc=now,UpdatedAtUtc=now});
        }
        var employee=await db.Employees.AsNoTracking().Where(e=>e.AcademyId==academy&&e.IsActive).OrderBy(e=>e.Id).FirstOrDefaultAsync();
        if(employee is not null)await Add("Employee",employee.Id,employee.Name,employee.BranchId,await db.Branches.Where(b=>b.Id==employee.BranchId).Select(b=>b.ArabicName).SingleAsync(),"مراجعة مستندات الموظف الاصطناعي وتجديدها؛ دون صرف أو خصم من هذه المتابعة.",1);
        var guardian=await db.GuardianProfiles.AsNoTracking().Where(g=>g.AcademyId==academy&&g.IsActive).OrderBy(g=>g.Id).FirstOrDefaultAsync();
        if(guardian is not null)await Add("Guardian",guardian.Id,guardian.DisplayName,null,null,"تجهيز مراجعة الأسرة الاصطناعية لجدول الأبناء؛ لا إرسال تلقائي ولا خصم من المتابعة.",2);
        await db.SaveChangesAsync();await tx.CommitAsync();
    }
}
