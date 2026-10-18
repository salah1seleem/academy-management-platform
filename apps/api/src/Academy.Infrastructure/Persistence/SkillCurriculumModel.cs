using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Structure;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<SkillCurriculum> SkillCurricula => Set<SkillCurriculum>();
    public DbSet<SkillRequirement> SkillRequirements => Set<SkillRequirement>();
    public DbSet<PlayerSkillPlan> PlayerSkillPlans => Set<PlayerSkillPlan>();
    public DbSet<PlayerSkillReview> PlayerSkillReviews => Set<PlayerSkillReview>();
    private static void ConfigureSkillCurricula(ModelBuilder b)
    {
        Tenant<SkillCurriculum>(b); Tenant<SkillRequirement>(b); Tenant<PlayerSkillPlan>(b); Tenant<PlayerSkillReview>(b);
        var c = b.Entity<SkillCurriculum>();
        c.ToTable("SkillCurricula");
        c.HasAlternateKey(x => new { x.AcademyId, x.Id, x.SportId });
        c.HasAlternateKey(x => new { x.AcademyId, x.Id, x.TrainingLevelId, x.SportId });
        c.HasIndex(x => new { x.AcademyId, x.TrainingLevelId, x.RevisionNumber }).IsUnique();
        c.HasIndex(x => new { x.AcademyId, x.CreatedByUserId, x.CreationRequestId }).IsUnique();
        c.HasOne<TrainingLevel>().WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingLevelId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.SportId }).OnDelete(DeleteBehavior.Restrict);
        c.HasOne<SkillCurriculum>().WithMany().HasForeignKey(x => new { x.AcademyId, x.CopiedFromId, x.TrainingLevelId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.TrainingLevelId, x.SportId }).OnDelete(DeleteBehavior.NoAction);
        c.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        c.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.PublishedByUserId).OnDelete(DeleteBehavior.Restrict);
        c.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.RetiredByUserId).OnDelete(DeleteBehavior.Restrict);
        c.Property(x => x.Title).HasMaxLength(100); c.Property(x => x.LevelNameSnapshot).HasMaxLength(100); c.Property(x => x.State).HasMaxLength(16); c.Property(x => x.CreationHash).HasMaxLength(64);
        c.ToTable(t => t.HasCheckConstraint("CK_SkillCurriculum_EditRevision", "\"EditRevision\">0"));
        c.ToTable(t => t.HasCheckConstraint("CK_SkillCurriculum_State", "\"IsActive\" AND \"RevisionNumber\">0 AND length(trim(\"Title\"))>0 AND \"State\" IN ('Draft','Published','Retired') AND (\"State\"<>'Published' OR \"PublishedByUserId\" IS NOT NULL AND \"PublishedAtUtc\" IS NOT NULL AND \"PublishRequestId\" IS NOT NULL) AND (\"State\"<>'Retired' OR \"RetiredByUserId\" IS NOT NULL AND \"RetiredAtUtc\" IS NOT NULL)"));
        var r = b.Entity<SkillRequirement>();
        r.HasAlternateKey(x => new { x.AcademyId, x.Id, x.CurriculumId, x.SportId });
        r.HasIndex(x => new { x.AcademyId, x.CurriculumId, x.DisplayOrder }).IsUnique();
        r.HasOne<SkillCurriculum>().WithMany().HasForeignKey(x => new { x.AcademyId, x.CurriculumId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.SportId }).OnDelete(DeleteBehavior.Restrict);
        r.Property(x => x.Name).HasMaxLength(120); r.Property(x => x.Protocol).HasMaxLength(500); r.Property(x => x.Unit).HasMaxLength(12);
        r.ToTable(t => t.HasCheckConstraint("CK_SkillRequirement_Protocol", "\"IsActive\" AND length(trim(\"Name\"))>0 AND length(trim(\"Protocol\"))>0 AND \"Unit\" IN ('Checklist','Count','Seconds','Metres') AND \"DisplayOrder\" BETWEEN 0 AND 19"));
        var p = b.Entity<PlayerSkillPlan>();
        p.HasAlternateKey(x => new { x.AcademyId, x.Id, x.CurriculumId, x.SportId });
        p.HasIndex(x => new { x.AcademyId, x.SportEnrollmentId }).IsUnique().HasFilter("\"IsActive\"");
        p.HasIndex(x => new { x.AcademyId, x.AssignedByUserId, x.RequestId }).IsUnique();
        p.HasOne<SportEnrollment>().WithMany().HasForeignKey(x => new { x.AcademyId, x.SportEnrollmentId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.SportId }).OnDelete(DeleteBehavior.Restrict);
        p.HasOne<SkillCurriculum>().WithMany().HasForeignKey(x => new { x.AcademyId, x.CurriculumId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.SportId }).OnDelete(DeleteBehavior.Restrict);
        p.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.AssignedByUserId).OnDelete(DeleteBehavior.Restrict); p.Property(x => x.RequestHash).HasMaxLength(64);
        p.ToTable(t => t.HasCheckConstraint("CK_PlayerSkillPlan_State", "(\"IsActive\" AND \"EndedAtUtc\" IS NULL) OR (NOT \"IsActive\" AND \"EndedAtUtc\" IS NOT NULL)"));
        var v = b.Entity<PlayerSkillReview>();
        v.HasOne<PlayerSkillPlan>().WithMany().HasForeignKey(x => new { x.AcademyId, x.PlanId, x.CurriculumId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.CurriculumId, x.SportId }).OnDelete(DeleteBehavior.Restrict);
        v.HasOne<SkillRequirement>().WithMany().HasForeignKey(x => new { x.AcademyId, x.RequirementId, x.CurriculumId, x.SportId }).HasPrincipalKey(x => new { x.AcademyId, x.Id, x.CurriculumId, x.SportId }).OnDelete(DeleteBehavior.Restrict);
        v.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        v.HasIndex(x => new { x.AcademyId, x.ActorUserId, x.RequestId }).IsUnique(); v.HasIndex(x => new { x.AcademyId, x.PlanId, x.Sequence }).IsUnique();
        v.HasIndex(x => new { x.AcademyId, x.PlanId, x.RequirementId, x.ObservedOn, x.Sequence });
        v.Property(x => x.ActorName).HasMaxLength(160); v.Property(x => x.State).HasMaxLength(16); v.Property(x => x.Note).HasMaxLength(500); v.Property(x => x.RequestHash).HasMaxLength(64); v.Property(x => x.MeasuredValue).HasPrecision(12, 3);
        v.ToTable(t => t.HasCheckConstraint("CK_PlayerSkillReview_Value", "\"IsActive\" AND \"Sequence\">0 AND \"State\" IN ('NotAssessed','Learning','Mastered') AND (\"MeasuredValue\" IS NULL OR \"MeasuredValue\" BETWEEN 0 AND 1000000) AND (\"State\"<>'NotAssessed' OR \"MeasuredValue\" IS NULL)"));
    }
    private async Task GuardSkillHistory(CancellationToken ct)
    {
        if (ChangeTracker.Entries<PlayerSkillReview>().Any(e => e.State is EntityState.Modified or EntityState.Deleted)) throw new InvalidOperationException("ملاحظات المهارات ثابتة؛ التصحيح بملاحظة جديدة.");
        foreach (var e in ChangeTracker.Entries<PlayerSkillPlan>().Where(e => e.State is EntityState.Modified or EntityState.Deleted))
            if (e.State == EntityState.Deleted || !e.OriginalValues.GetValue<bool>("IsActive") || e.Entity.IsActive || e.Entity.EndedAtUtc == null || e.Properties.Any(p => p.IsModified && p.Metadata.Name is not ("IsActive" or "EndedAtUtc" or "UpdatedAtUtc"))) throw new InvalidOperationException("إسناد المنهج محفوظ؛ يمكن استبداله بإسناد صريح جديد فقط.");
        foreach (var e in ChangeTracker.Entries<SkillCurriculum>().Where(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            var original = e.OriginalValues.GetValue<string>("State");
            if (e.State == EntityState.Deleted || new[] { "Id", "AcademyId", "SportId", "TrainingLevelId", "RevisionNumber", "CopiedFromId", "CreatedByUserId", "CreationRequestId", "CreationHash", "CreatedAtUtc", "IsActive" }.Any(p => e.Property(p).IsModified)
                || e.Entity.EditRevision != e.OriginalValues.GetValue<int>("EditRevision") + 1
                || original != "Draft" && (original != "Published" || e.Entity.State != "Retired" || e.Properties.Any(p => p.IsModified && p.Metadata.Name is not ("State" or "RetiredAtUtc" or "RetiredByUserId" or "EditRevision" or "UpdatedAtUtc")))) throw new InvalidOperationException("نسخة المنهج المنشورة ثابتة؛ أنشئ مسودة نسخة جديدة.");
        }
        var entries = ChangeTracker.Entries<SkillRequirement>().Where(e => e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted).ToArray();
        if (entries.Length == 0) return;
        var ids = entries.Select(e => e.Entity.CurriculumId).Distinct().ToArray();
        var states = await SkillCurricula.AsNoTracking().Where(c => ids.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.State, ct);
        foreach (var e in entries)
        {
            var tracked = ChangeTracker.Entries<SkillCurriculum>().FirstOrDefault(c => c.Entity.Id == e.Entity.CurriculumId);
            var state = tracked?.State == EntityState.Added ? "Draft" : states.GetValueOrDefault(e.Entity.CurriculumId);
            if (state != "Draft" || e.State == EntityState.Modified && new[] { "Id", "AcademyId", "CurriculumId", "SportId" }.Any(p => e.Property(p).IsModified)) throw new InvalidOperationException("متطلبات النسخة المنشورة ثابتة.");
        }
    }
}
