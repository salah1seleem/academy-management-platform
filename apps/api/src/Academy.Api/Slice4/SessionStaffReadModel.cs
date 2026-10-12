using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice4;
public sealed record AssignedSessionStaff(Guid Id,string Name,AcademyRole Role);
public static class SessionStaffReadModel
{
    public static async Task<Dictionary<Guid,AssignedSessionStaff[]>> Read(FoundationDbContext db,Guid academy,Guid[] sessions,Guid[] groups)
    {
        var planned=await db.StaffGroupAssignments.Where(a=>a.AcademyId==academy&&groups.Contains(a.TrainingGroupId)&&a.IsActive).Select(a=>new{a.TrainingGroupId,a.AcademyMembershipId}).ToArrayAsync();
        var overrides=await db.SessionCoachOverrides.Where(o=>o.AcademyId==academy&&sessions.Contains(o.TrainingSessionId)).Select(o=>new{o.TrainingSessionId,o.ActualCoachMembershipId}).ToArrayAsync();
        var sourceGroups=await db.TrainingSessions.Where(s=>s.AcademyId==academy&&sessions.Contains(s.Id)).Select(s=>new{s.Id,s.TrainingGroupId}).ToDictionaryAsync(s=>s.Id,s=>s.TrainingGroupId);
        var result=new Dictionary<Guid,AssignedSessionStaff[]>();
        foreach(var session in sessions) {
            var candidates=planned.Where(a=>a.TrainingGroupId==sourceGroups.GetValueOrDefault(session)).Select(a=>a.AcademyMembershipId).Concat(overrides.Where(o=>o.TrainingSessionId==session).Select(o=>o.ActualCoachMembershipId)).Distinct().ToArray();
            result[session]=await db.AcademyMemberships.AsNoTracking().Where(m=>m.AcademyId==academy&&candidates.Contains(m.Id)&&FoundationDbContext.IsSessionStaffAssigned(academy,m.Id,session)).OrderBy(m=>m.User.DisplayName).Select(m=>new AssignedSessionStaff(m.Id,m.User.DisplayName,m.Role)).ToArrayAsync();
        }
        return result;
    }
}
