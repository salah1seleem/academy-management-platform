using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice4;
using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
public static partial class OwnerFollowUpEndpoints
{
    private sealed record RecordSource(Guid Id,string Name,Guid? BranchId,string? Branch,Guid? SportId,string? Sport);
    private static string RecordModule(string kind)=>kind switch{"Employee"=>"payroll","Receivable" or "Request"=>"finance",_=>"players"};
    private static string RecordUrl(OwnerFollowUp f)=>f.SourceKind switch{"Player"=>$"/dashboard/players/{f.SourceId}","Guardian"=>$"/dashboard/guardians/{f.SourceId}","Employee"=>$"/dashboard/staff/employees/{f.SourceId}","Request"=>$"/dashboard/feedback/{f.SourceId}","Receivable"=>$"/dashboard/finance/receivables/{f.SourceId}",_=>$"/dashboard/academy/groups/{f.TrainingGroupId}"};
    private static object? RecordDisplay(OwnerFollowUp f)=>f.SourceKind=="Group"?null:new{kind=f.SourceKind,name=f.SourceNameSnapshot,branch=f.SourceBranchSnapshot,sport=f.SourceSportSnapshot};
    private static async Task<IQueryable<OwnerFollowUp>> ReadableFollowUps(FoundationDbContext db,TenantMembership t,HttpContext h){var payroll=await h.RequestServices.GetRequiredService<Academy.Api.Configuration.AcademyConfigurationService>().Enabled(t.AcademyId,"payroll");return db.OwnerFollowUps.Where(f=>(f.SourceKind!="Employee"||payroll)&&(t.Role==AcademyRole.AcademyOwner||f.SourceKind=="Group"||db.StaffActionGrants.Any(g=>g.MembershipId==t.MembershipId&&g.IsActive&&g.Read&&g.Module==(f.SourceKind=="Employee"?"payroll":f.SourceKind=="Request"||f.SourceKind=="Receivable"?"finance":"players"))));}
    private static async Task<GroupSignal?> FollowUpSignal(FoundationDbContext db,Guid academy,DateOnly today,OwnerFollowUp f,DateTimeOffset now)=>f.TrainingGroupId.HasValue?(await Signals(db,academy,today,f.TrainingGroupId.Value,now)).SingleOrDefault():null;
    private static async Task<object[]> RecordOwners(FoundationDbContext db,Guid academy,OwnerFollowUp f){var result=new List<object>();foreach(var m in await db.AcademyMemberships.Where(m=>m.AcademyId==academy&&m.IsActive&&m.User.IsActive).Select(m=>new{m.Id,name=m.User.DisplayName}).ToArrayAsync())if(await EligibleRecordOwner(db,academy,m.Id,f))result.Add(m);return result.ToArray();}
    private static async Task<RecordSource[]> RecordSources(FoundationDbContext db,Guid academy,string kind,string? search,Guid? id=null)
    {
        var term=search?.Trim()??"";
        return kind switch{
            "Player"=>await db.SportEnrollments.AsNoTracking().Where(e=>e.AcademyId==academy&&e.IsActive&&e.Player.IsActive&&(!id.HasValue||e.PlayerId==id)&&(term==""||e.Player.ArabicName.Contains(term)||e.Player.PlayerCode.Contains(term))).OrderBy(e=>e.Player.ArabicName).ThenBy(e=>e.BranchId).ThenBy(e=>e.SportId).Take(50).Select(e=>new RecordSource(e.PlayerId,e.Player.ArabicName,e.BranchId,e.Branch.ArabicName,e.SportId,e.Sport.ArabicName)).ToArrayAsync(),
            "Employee"=>await db.Employees.AsNoTracking().Where(e=>e.AcademyId==academy&&e.IsActive&&(!id.HasValue||e.Id==id)&&(term==""||e.Name.Contains(term)||e.Phone.Contains(term))).OrderBy(e=>e.Name).Take(50).Select(e=>new RecordSource(e.Id,e.Name,e.BranchId,db.Branches.Where(b=>b.Id==e.BranchId).Select(b=>b.ArabicName).FirstOrDefault(),null,null)).ToArrayAsync(),
            "Guardian"=>await db.GuardianProfiles.AsNoTracking().Where(g=>g.AcademyId==academy&&g.IsActive&&(!id.HasValue||g.Id==id)&&(term==""||g.DisplayName.Contains(term)||g.ContactPhone!=null&&g.ContactPhone.Contains(term))).OrderBy(g=>g.DisplayName).Take(50).Select(g=>new RecordSource(g.Id,g.DisplayName,null,null,null,null)).ToArrayAsync(),
            "Request"=>await db.GuardianFeedbacks.AsNoTracking().Where(r=>r.AcademyId==academy&&r.IsActive&&r.Kind!="Rating"&&(!id.HasValue||r.Id==id)&&(term==""||db.Players.Any(p=>p.Id==r.PlayerId&&p.ArabicName.Contains(term)))).OrderByDescending(r=>r.CreatedAtUtc).Take(50).Select(r=>new RecordSource(r.Id,"طلب أسرة · "+db.Players.Where(p=>p.Id==r.PlayerId).Select(p=>p.ArabicName).FirstOrDefault(),r.BranchId,db.Branches.Where(b=>b.Id==r.BranchId).Select(b=>b.ArabicName).FirstOrDefault(),db.TrainingGroups.Where(g=>g.Id==r.TrainingGroupId).Select(g=>(Guid?)g.SportId).FirstOrDefault(),db.TrainingGroups.Where(g=>g.Id==r.TrainingGroupId).Select(g=>g.Sport.ArabicName).FirstOrDefault())).ToArrayAsync(),
            "Receivable"=>await db.ReceivableDocuments.AsNoTracking().Where(r=>r.AcademyId==academy&&(!id.HasValue||r.Id==id)&&(term==""||r.PlayerNameSnapshot.Contains(term))).OrderBy(r=>r.DueDate).Take(50).Select(r=>new RecordSource(r.Id,"مطالبة · "+r.PlayerNameSnapshot,r.BranchId,db.Branches.Where(b=>b.Id==r.BranchId).Select(b=>b.ArabicName).FirstOrDefault(),db.SportEnrollments.Where(e=>e.Id==r.SportEnrollmentId).Select(e=>(Guid?)e.SportId).FirstOrDefault(),db.SportEnrollments.Where(e=>e.Id==r.SportEnrollmentId).Select(e=>e.Sport.ArabicName).FirstOrDefault())).ToArrayAsync(),
            _=>[]
        };
    }
    private static async Task<IResult> RecordOptions(string kind,string? search,Guid? sourceId,Guid? branchId,Guid? sportId,CurrentTenant tenant,FoundationDbContext db,HttpContext http)
    {
        if(!await Owner(tenant,db,http))return Results.Forbid();var t=(await tenant.ResolveAsync())!;
        if(kind=="Employee"&&!await http.RequestServices.GetRequiredService<Academy.Api.Configuration.AcademyConfigurationService>().Enabled(t.AcademyId,"payroll"))return Results.Forbid();
        if(kind is not("Player" or "Employee" or "Guardian" or "Request" or "Receivable")||search?.Length>120)return Invalid("اختر نوع السجل وابحث بالاسم.");
        var items=await RecordSources(db,t.AcademyId,kind,search,sourceId);
        var selected=items.FirstOrDefault(s=>s.Id==sourceId&&s.BranchId==branchId&&s.SportId==sportId);
        var owners=new List<object>();if(selected is not null){var stub=new OwnerFollowUp{SourceKind=kind,SourceId=selected.Id,SourceBranchId=selected.BranchId,SourceSportId=selected.SportId,Condition="RecordFollowUp",State="New",EvidenceJson="{}",RequestHash=""};foreach(var m in await db.AcademyMemberships.Where(m=>m.AcademyId==t.AcademyId&&m.IsActive&&m.User.IsActive).Select(m=>new{m.Id,name=m.User.DisplayName}).ToArrayAsync())if(await EligibleRecordOwner(db,t.AcademyId,m.Id,stub))owners.Add(m);}
        return Results.Ok(new{items,owners,limit=50,notice="ابحث لتضييق النتائج؛ الربط لا ينفذ إجراءً على السجل."});
    }
    private static async Task<bool> EligibleRecordOwner(FoundationDbContext db,Guid academy,Guid member,OwnerFollowUp f)
    {
        var m=await db.AcademyMemberships.AsNoTracking().SingleOrDefaultAsync(m=>m.AcademyId==academy&&m.Id==member&&m.IsActive&&m.User.IsActive);if(m is null)return false;if(m.Role==AcademyRole.AcademyOwner)return true;
        return await DelegateAllowed(db,academy,member,true)
            &&(m.AllBranches||f.SourceBranchId.HasValue&&await db.MembershipBranchAccesses.AnyAsync(a=>a.MembershipId==member&&a.IsActive&&a.BranchId==f.SourceBranchId))
            &&(m.AllSports||f.SourceSportId.HasValue&&await db.MembershipSportAccesses.AnyAsync(a=>a.MembershipId==member&&a.IsActive&&a.SportId==f.SourceSportId))
            &&await db.StaffActionGrants.AnyAsync(g=>g.MembershipId==member&&g.Module==RecordModule(f.SourceKind)&&g.IsActive&&g.Read);
    }
    private static async Task<IResult> CreateRecord(RecordFollowUpCreate b,CurrentTenant tenant,FoundationDbContext db,TimeProvider clock,HttpContext http)
    {
        var t=(await tenant.ResolveAsync())!;await using var tx=await db.Database.BeginTransactionAsync();
        if(!await Owner(tenant,db,http,b.AssignedToMembershipId,true))return Results.Forbid();
        if(b.Kind=="Employee"&&!await http.RequestServices.GetRequiredService<Academy.Api.Configuration.AcademyConfigurationService>().Enabled(t.AcademyId,"payroll"))return Results.Forbid();
        var today=await AttendanceCalendar.TodayAsync(t.AcademyId,db,clock);
        if(b.RequestId==Guid.Empty||b.SourceId==Guid.Empty||b.Priority is <1 or >2||b.Note?.Length>500||b.DueOn<today)return Invalid("اختر السجل وأولوية صحيحة وملاحظة حتى500حرف وموعدًا من اليوم، أو اتركهما فارغين.");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"followup-create:{t.AcademyId}:{t.MembershipId}:{b.RequestId}"},0))");
        var hash=Hash(b);var prior=await db.OwnerFollowUps.SingleOrDefaultAsync(f=>f.CreatedByMembershipId==t.MembershipId&&f.RequestId==b.RequestId);if(prior is not null)return prior.RequestHash==hash?Results.Ok(new{prior.Id,replay=true}):Results.Conflict(new{message="المحاولة مستخدمة لبيانات أخرى."});
        var source=(await RecordSources(db,t.AcademyId,b.Kind,null,b.SourceId)).FirstOrDefault(s=>s.BranchId==b.BranchId&&s.SportId==b.SportId);if(source is null)return Results.NotFound();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"followup-source:{t.AcademyId}:{b.SourceId}:RecordFollowUp"},0))");
        var existing=await db.OwnerFollowUps.Where(f=>f.SourceKind==b.Kind&&f.SourceId==b.SourceId&&f.State!="Closed"&&f.State!="Rejected").Select(f=>(Guid?)f.Id).FirstOrDefaultAsync();if(existing.HasValue)return Results.Conflict(new{message="للسجل متابعة مفتوحة بالفعل.",id=existing});
        var now=clock.GetUtcNow();var row=new OwnerFollowUp{Id=Guid.NewGuid(),AcademyId=t.AcademyId,SourceKind=b.Kind,SourceId=source.Id,SourceNameSnapshot=source.Name,SourceBranchId=source.BranchId,SourceSportId=source.SportId,SourceBranchSnapshot=source.Branch,SourceSportSnapshot=source.Sport,ManualPriority=b.Priority,Condition="RecordFollowUp",State=b.AssignedToMembershipId.HasValue?"Assigned":"New",CreatedByMembershipId=t.MembershipId,AssignedToMembershipId=b.AssignedToMembershipId,CreatedOn=today,DueOn=b.DueOn,Note=b.Note?.Trim(),RequestId=b.RequestId,RequestHash=hash,EvidenceJson=JsonSerializer.Serialize(new{source,sourceKind=b.Kind,observedAtUtc=now}),CreatedAtUtc=now,UpdatedAtUtc=now};
        if(b.AssignedToMembershipId.HasValue&&!await EligibleRecordOwner(db,t.AcademyId,b.AssignedToMembershipId.Value,row))return Invalid("اختر مسؤولًا مخولًا بالسجل والفرع والرياضة.");
        db.OwnerFollowUps.Add(row);db.OwnerFollowUpChanges.Add(new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,FollowUpId=row.Id,ActorMembershipId=t.MembershipId,RequestId=b.RequestId,RequestHash=hash,BeforeJson="{}",AfterJson=JsonSerializer.Serialize(Snapshot(row)),CreatedAtUtc=now,UpdatedAtUtc=now});
        await db.SaveChangesAsync();await tx.CommitAsync();return Results.Created($"/api/v1/manage/owner-followups/{row.Id}",new{row.Id});
    }
}
public sealed record RecordFollowUpCreate(string Kind,Guid SourceId,Guid RequestId,Guid? BranchId=null,Guid? SportId=null,Guid? AssignedToMembershipId=null,DateOnly? DueOn=null,string? Note=null,int Priority=1);
