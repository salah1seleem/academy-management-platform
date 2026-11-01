using Academy.Api.Auth;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice5;

public static partial class Slice5Endpoints
{
    public sealed record EvaluationSetupWrite(Guid SportId);
    private sealed record SuggestedCriterion(string ArabicName, FootballAxis? FootballAxis = null);
    private static SuggestedCriterion[] Suggested(SportKind kind) => kind switch {
        SportKind.Football => [new("دقة التمرير",FootballAxis.Passing),new("التحكم بالكرة",FootballAxis.Dribbling),new("السرعة",FootballAxis.Speed),new("التمركز الدفاعي",FootballAxis.Defending),new("التحمل",FootballAxis.Physical),new("دقة التسديد",FootballAxis.Shooting)],
        SportKind.Swimming => [new("تقنية التنفس"),new("وضع الجسم"),new("حركة الذراعين"),new("ضربات الرجلين"),new("التحمل"),new("الانطلاقة"),new("الدوران")],
        _ => [new("المهارة الفنية"),new("التوافق الحركي"),new("التحمل"),new("الالتزام بالتدريب")]
    };
    private static async Task<IResult> PreviewCriteriaDefaults(Guid sportId, CurrentTenant tenant, FoundationDbContext db)
    {
        var t=(await tenant.ResolveAsync())!;
        var sport=await db.Sports.AsNoTracking().SingleOrDefaultAsync(s=>s.Id==sportId&&s.AcademyId==t.AcademyId&&s.IsActive);
        if(sport is null)return Results.NotFound();
        return Results.Ok(new{sportId,sport=sport.ArabicName,criteria=Suggested(sport.Kind),existing=await db.EvaluationCriteria.AnyAsync(c=>c.SportId==sportId&&c.AcademyId==t.AcademyId)});
    }
    private static async Task<IResult> SaveCriteriaDefaults(EvaluationSetupWrite body, CurrentTenant tenant, FoundationDbContext db, TimeProvider time)
    {
        var t=(await tenant.ResolveAsync())!;
        await using var tx=await db.Database.BeginTransactionAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"evaluation-criteria:{t.AcademyId}:{body.SportId}"},0))");
        var sport=await db.Sports.SingleOrDefaultAsync(s=>s.Id==body.SportId&&s.AcademyId==t.AcademyId&&s.IsActive);
        if(sport is null)return Results.NotFound();
        var existing=await db.EvaluationCriteria.Where(c=>c.AcademyId==t.AcademyId&&c.SportId==sport.Id).ToListAsync();
        if(existing.Count>0){
            if(!existing.Any(c=>c.IsActive))return Results.Conflict(new{code="evaluation_criteria_inactive",message="معايير الرياضة متوقفة. راجع إعداداتها قبل إنشاء تقييم."});
            return Results.Ok(new{count=existing.Count(c=>c.IsActive),created=0});
        }
        var now=time.GetUtcNow();var items=Suggested(sport.Kind);
        for(var i=0;i<items.Length;i++)db.EvaluationCriteria.Add(new(){Id=Guid.NewGuid(),AcademyId=t.AcademyId,SportId=sport.Id,ArabicName=items[i].ArabicName,FootballAxis=items[i].FootballAxis,Weight=1,DisplayOrder=i+1,CreatedAtUtc=now,UpdatedAtUtc=now});
        await db.SaveChangesAsync();await tx.CommitAsync();return Results.Ok(new{count=items.Length,created=items.Length});
    }
}
