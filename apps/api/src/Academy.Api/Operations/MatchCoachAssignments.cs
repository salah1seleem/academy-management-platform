using Academy.Infrastructure.Operations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public static class MatchCoachAssignments
{
    internal static IQueryable<AcademyMatch> ForCoach(IQueryable<AcademyMatch> query,FoundationDbContext db,Guid coach,bool activeLegacy=false)=>query.Where(m=>
        db.MatchCoachAssignments.Any(a=>a.MatchId==m.Id&&a.CoachMembershipId==coach&&!db.MatchCoachAssignments.Any(n=>n.MatchId==m.Id&&n.Sequence>a.Sequence)) ||
        !db.MatchCoachAssignments.Any(a=>a.MatchId==m.Id)&&db.MatchTeams.Any(t=>t.Id==m.TeamId&&t.CoachMembershipId==coach&&(!activeLegacy||t.IsActive)));
    internal static async Task<string?> Apply(FoundationDbContext db,AcademyMatch match,Guid coach,int? expected,Guid actor,DateTimeOffset now,string? note,bool publishing)
    {
        if(note?.Length>300)return "ملاحظة الإسناد لا تتجاوز 300 حرف.";
        var previous=await db.MatchCoachAssignments.Where(x=>x.MatchId==match.Id).OrderByDescending(x=>x.Sequence).FirstOrDefaultAsync();
        if(previous?.CoachMembershipId==coach)return null; // Exact retry never duplicates the assignment.
        if(expected!=(previous?.Sequence??0))return "تغير إسناد المدرب؛ حدّث المباراة قبل اختيار مدرب آخر.";
        db.MatchCoachAssignments.Add(new(){Id=Guid.NewGuid(),AcademyId=match.AcademyId,MatchId=match.Id,CoachMembershipId=coach,AssignedByMembershipId=actor,Sequence=(previous?.Sequence??0)+1,
            Source=previous is not null?"Reassignment":match.PublishedAtUtc.HasValue?"LegacyReview":publishing?"FirstPublication":"DraftSelection",Note=string.IsNullOrWhiteSpace(note)?null:note.Trim(),CreatedAtUtc=now,UpdatedAtUtc=now});
        return null;
    }
}
