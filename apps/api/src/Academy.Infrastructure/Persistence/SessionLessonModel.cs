using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Evaluations;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Structure;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;
public sealed partial class FoundationDbContext
{
    public DbSet<TrainingExercise> TrainingExercises => Set<TrainingExercise>();
    public DbSet<SessionLessonTemplate> SessionLessonTemplates => Set<SessionLessonTemplate>();
    public DbSet<SessionLessonStep> SessionLessonSteps => Set<SessionLessonStep>();
    public DbSet<SessionLessonAssignment> SessionLessonAssignments => Set<SessionLessonAssignment>();
    public DbSet<TrainingContentArchive> TrainingContentArchives => Set<TrainingContentArchive>();
    private static void ConfigureSessionLessons(ModelBuilder b)
    {
        Tenant<TrainingExercise>(b); Tenant<SessionLessonTemplate>(b); Tenant<SessionLessonStep>(b); Tenant<SessionLessonAssignment>(b);
        var e=b.Entity<TrainingExercise>();
        e.HasAlternateKey(x=>new{x.AcademyId,x.Id,x.TrainingLevelId,x.SportId});
        e.HasIndex(x=>new{x.AcademyId,x.CreatedByUserId,x.RequestId}).IsUnique();
        e.HasOne<TrainingLevel>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.TrainingLevelId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.SportId}).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<TrainingExercise>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.CopiedFromId,x.TrainingLevelId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.TrainingLevelId,x.SportId}).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
        e.Property(x=>x.Title).HasMaxLength(100);e.Property(x=>x.Skill).HasMaxLength(120);e.Property(x=>x.Instructions).HasMaxLength(2000);e.Property(x=>x.Equipment).HasMaxLength(300);e.Property(x=>x.RightsKind).HasMaxLength(12);e.Property(x=>x.RightsReference).HasMaxLength(500);e.Property(x=>x.RequestHash).HasMaxLength(64);e.Property(x=>x.State).HasMaxLength(12);
        e.ToTable(t=>t.HasCheckConstraint("CK_TrainingExercise_Review","\"IsActive\" AND \"Minutes\" BETWEEN 1 AND 180 AND \"RightsKind\" IN ('Original','Licensed') AND length(trim(\"RightsReference\"))>0 AND \"State\" IN ('Draft','Reviewed') AND (\"State\"<>'Reviewed' OR \"ReviewedByUserId\" IS NOT NULL AND \"ReviewedAtUtc\" IS NOT NULL)"));
        var p=b.Entity<SessionLessonTemplate>();
        p.HasAlternateKey(x=>new{x.AcademyId,x.Id,x.TrainingLevelId,x.SportId});p.HasAlternateKey(x=>new{x.AcademyId,x.Id,x.SportId});
        p.HasIndex(x=>new{x.AcademyId,x.CreatedByUserId,x.RequestId}).IsUnique();
        p.HasOne<TrainingLevel>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.TrainingLevelId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.SportId}).OnDelete(DeleteBehavior.Restrict);
        p.HasOne<SessionLessonTemplate>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.CopiedFromId,x.TrainingLevelId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.TrainingLevelId,x.SportId}).OnDelete(DeleteBehavior.Restrict);
        p.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);p.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ReviewedByUserId).OnDelete(DeleteBehavior.Restrict);
        p.Property(x=>x.Title).HasMaxLength(100);p.Property(x=>x.RequestHash).HasMaxLength(64);p.Property(x=>x.State).HasMaxLength(12);
        p.ToTable(t=>t.HasCheckConstraint("CK_SessionLessonTemplate_Review","\"IsActive\" AND \"DeclaredMinutes\" BETWEEN 1 AND 180 AND \"State\" IN ('Draft','Reviewed') AND (\"State\"<>'Reviewed' OR \"ReviewedByUserId\" IS NOT NULL AND \"ReviewedAtUtc\" IS NOT NULL)"));
        var s=b.Entity<SessionLessonStep>();s.HasIndex(x=>new{x.AcademyId,x.TemplateId,x.Position}).IsUnique();
        s.HasOne<SessionLessonTemplate>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.TemplateId,x.TrainingLevelId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.TrainingLevelId,x.SportId}).OnDelete(DeleteBehavior.Restrict);
        s.HasOne<TrainingExercise>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.ExerciseId,x.TrainingLevelId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.TrainingLevelId,x.SportId}).OnDelete(DeleteBehavior.Restrict);
        s.ToTable(t=>t.HasCheckConstraint("CK_SessionLessonStep_Range","\"IsActive\" AND \"Minutes\" BETWEEN 1 AND 180 AND \"Position\" BETWEEN 0 AND 19"));
        var a=b.Entity<SessionLessonAssignment>();a.HasIndex(x=>new{x.AcademyId,x.TrainingSessionId,x.Revision}).IsUnique();a.HasIndex(x=>new{x.AcademyId,x.ActorUserId,x.RequestId}).IsUnique();
        a.HasOne<TrainingSession>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.TrainingSessionId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.SportId}).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<SessionLessonTemplate>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.TemplateId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.SportId}).OnDelete(DeleteBehavior.Restrict);
        a.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ActorUserId).OnDelete(DeleteBehavior.Restrict);a.Property(x=>x.ActorName).HasMaxLength(160);a.Property(x=>x.RequestHash).HasMaxLength(64);
        a.ToTable(t=>t.HasCheckConstraint("CK_SessionLessonAssignment_Revision","\"IsActive\" AND \"Revision\">0"));
        Tenant<TrainingContentArchive>(b);var r=b.Entity<TrainingContentArchive>();
        r.HasOne<TrainingExercise>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.ExerciseId,x.TrainingLevelId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.TrainingLevelId,x.SportId}).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<SessionLessonTemplate>().WithMany().HasForeignKey(x=>new{x.AcademyId,x.TemplateId,x.TrainingLevelId,x.SportId}).HasPrincipalKey(x=>new{x.AcademyId,x.Id,x.TrainingLevelId,x.SportId}).OnDelete(DeleteBehavior.Restrict);
        r.HasOne<ApplicationUser>().WithMany().HasForeignKey(x=>x.ActorUserId).OnDelete(DeleteBehavior.Restrict);
        r.HasIndex(x=>new{x.AcademyId,x.ExerciseId}).IsUnique();r.HasIndex(x=>new{x.AcademyId,x.TemplateId}).IsUnique();r.HasIndex(x=>new{x.AcademyId,x.ActorUserId,x.RequestId}).IsUnique();r.Property(x=>x.ActorName).HasMaxLength(160);r.Property(x=>x.Note).HasMaxLength(500);r.Property(x=>x.RequestHash).HasMaxLength(64);
        r.ToTable(t=>t.HasCheckConstraint("CK_TrainingContentArchive_Source","\"IsActive\" AND ((\"ExerciseId\" IS NOT NULL AND \"TemplateId\" IS NULL) OR (\"ExerciseId\" IS NULL AND \"TemplateId\" IS NOT NULL))"));
    }
    private void GuardSessionLessons()
    {
        if(ChangeTracker.Entries<TrainingContentArchive>().Any(e=>e.State is EntityState.Modified or EntityState.Deleted))throw new InvalidOperationException("واقعة الأرشفة محفوظة؛ التصحيح بنسخة محتوى جديدة.");
        foreach(var entry in ChangeTracker.Entries().Where(e=>e.Entity is TrainingExercise or SessionLessonTemplate or SessionLessonStep or SessionLessonAssignment))
        {
            if(entry.State==EntityState.Deleted)throw new InvalidOperationException("خطة الحصة محفوظة؛ أنشئ نسخة جديدة.");
            if(entry.State!=EntityState.Modified)continue;
            if(entry.Entity is not (TrainingExercise or SessionLessonTemplate) || entry.OriginalValues.GetValue<string>("State")!="Draft" || entry.CurrentValues.GetValue<string>("State")!="Reviewed" || entry.Properties.Any(p=>p.IsModified && p.Metadata.Name is not ("State" or "ReviewedByUserId" or "ReviewedAtUtc" or "UpdatedAtUtc")))throw new InvalidOperationException("نسخة التدريب ثابتة؛ التصحيح بنسخة جديدة.");
        }
    }
}
