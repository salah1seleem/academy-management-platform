using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Operations;
public static class MatchAccess
{
    public static async Task<bool> Read(FoundationDbContext db,TenantMembership t)
    {
        if(t.Role==AcademyRole.AcademyOwner)return true;
        if(t.Role==AcademyRole.Coach)return await db.MatchTeams.AnyAsync(team=>team.AcademyId==t.AcademyId&&team.IsActive&&team.CoachCanManageMatches&&(team.CoachMembershipId==t.MembershipId||db.AcademyMatches.Any(m=>m.TeamId==team.Id&&db.MatchCoachAssignments.Any(a=>a.MatchId==m.Id&&a.CoachMembershipId==t.MembershipId&&!db.MatchCoachAssignments.Any(l=>l.MatchId==m.Id&&l.Sequence>a.Sequence)))));
        if(t.Role!=AcademyRole.AcademyAdmin)return false;
        var modern=await db.AcademyMemberships.Where(m=>m.Id==t.MembershipId&&m.IsActive).Select(m=>m.UseActionGrants).SingleAsync();
        return modern?await db.StaffActionGrants.AnyAsync(g=>g.MembershipId==t.MembershipId&&g.IsActive&&g.Module=="matches"&&g.Read):!await db.StaffGrants.AnyAsync(g=>g.MembershipId==t.MembershipId&&g.IsActive&&g.Permission==AcademyPermissions.StructureManage&&!g.Allowed);
    }
    public static async Task<bool> Write(FoundationDbContext db,TenantMembership t,bool edit)
    {
        if(!await Read(db,t))return false;
        if(t.Role==AcademyRole.Coach&&!edit)return await db.MatchTeams.AnyAsync(team=>team.IsActive&&team.CoachCanManageMatches&&team.CoachMembershipId==t.MembershipId);
        if(t.Role!=AcademyRole.AcademyAdmin)return true;
        if(!await db.AcademyMemberships.Where(m=>m.Id==t.MembershipId).Select(m=>m.UseActionGrants).SingleAsync())return true;
        return await db.StaffActionGrants.AnyAsync(g=>g.MembershipId==t.MembershipId&&g.IsActive&&g.Module=="matches"&&g.Read&&(edit?g.Edit:g.Add));
    }
}
