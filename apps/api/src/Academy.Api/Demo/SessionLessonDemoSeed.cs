using Academy.Api.Auth;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Demo;
public static class SessionLessonDemoSeed
{
    public static async Task Seed(FoundationDbContext db,IHostEnvironment env,DemoOptions options)
    {
        var academy=FootballDemoSeed.AcademyId;
        if(!env.IsEnvironment("Demo")||!options.SeedEnabled||options.SeedProfile!="Football"||!await db.Academies.AnyAsync(a=>a.Id==academy&&a.Slug==FootballDemoSeed.Slug&&a.EnglishName==FootballDemoSeed.Marker))throw new InvalidOperationException("خطط العرض تتطلب Football Demo معلوم الهوية.");
        await using var tx=await db.Database.BeginTransactionAsync();await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"football-session-lesson-demo"},0))");
        var id=FootballDemoSeed.Id("lesson-template/60");if(await db.SessionLessonTemplates.AnyAsync(p=>p.Id==id))return;
        var sport=await db.SportEnrollments.Where(e=>e.PlayerId==FootballDemoSeed.Id("player/10")&&e.AcademyId==academy).Select(e=>e.SportId).SingleAsync();var level=FootballDemoSeed.Id("lesson-template/level");var actor=FootballDemoSeed.Id("user/owner");var now=DateTimeOffset.Parse("2026-09-28T06:00:00Z");
        if(!await db.TrainingLevels.AnyAsync(l=>l.Id==level))db.TrainingLevels.Add(new(){Id=level,AcademyId=academy,SportId=sport,ArabicName="مستوى خطة حصة اصطناعية",Rank=20,CreatedAtUtc=now,UpdatedAtUtc=now});
        var durations=new[]{10,15,20,10,5};var titles=new[]{"افتتاح الحصة","تجهيز المهارة","تطبيق المهارة","نشاط جماعي","ختام الحصة"};
        for(var i=0;i<durations.Length;i++){var exercise=FootballDemoSeed.Id($"lesson-exercise/{i}");db.TrainingExercises.Add(new(){Id=exercise,AcademyId=academy,TrainingLevelId=level,SportId=sport,Title=titles[i]+" — عرض اصطناعي",Skill="مهارة يحددها المراجع الفني",Instructions="مثال واجهة اصطناعي غير معتمد للتدريب. يستبدله مختص بمحتوى أصلي أو مرخص وملائم للعمر والسلامة قبل الاستخدام.",Equipment="يحدد المختص الأدوات والقيود قبل الاعتماد",Minutes=durations[i],RightsKind="Original",RightsReference="نص عرض اصطناعي للمنصة؛ ليس منهجًا تدريبيًا معتمدًا",CreatedByUserId=actor,RequestId=exercise,RequestHash="synthetic-demo",CreatedAtUtc=now,UpdatedAtUtc=now});db.SessionLessonSteps.Add(new(){Id=FootballDemoSeed.Id($"lesson-step/{i}"),AcademyId=academy,TemplateId=id,ExerciseId=exercise,TrainingLevelId=level,SportId=sport,Position=i,Minutes=durations[i],CreatedAtUtc=now,UpdatedAtUtc=now});}
        db.SessionLessonTemplates.Add(new(){Id=id,AcademyId=academy,TrainingLevelId=level,SportId=sport,Title="حصة60 دقيقة — مسودة اصطناعية للمراجعة",DeclaredMinutes=60,CreatedByUserId=actor,RequestId=id,RequestHash="synthetic-demo",CreatedAtUtc=now,UpdatedAtUtc=now});await db.SaveChangesAsync();await tx.CommitAsync();
        // Never fabricate a human technical approval or assign/retime a real group.
    }
}
