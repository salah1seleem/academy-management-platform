using Academy.Infrastructure.Finance;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Structure;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    public DbSet<OperatingExpense> OperatingExpenses => Set<OperatingExpense>();
    public DbSet<OperatingExpenseAllocation> OperatingExpenseAllocations => Set<OperatingExpenseAllocation>();

    private static void ConfigureOperatingExpenses(ModelBuilder b)
    {
        Tenant<OperatingExpense>(b); Tenant<OperatingExpenseAllocation>(b);
        b.Entity<OperatingExpense>(e => {
            e.Property(x => x.Category).HasMaxLength(60);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.Note).HasMaxLength(400);
            e.Property(x => x.RequestKey).HasMaxLength(100);
            e.Property(x => x.RequestHash).HasMaxLength(64);
            e.Property(x => x.PostRequestKey).HasMaxLength(100);
            e.Property(x => x.PostQuoteHash).HasMaxLength(64);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.HasOne<Branch>().WithMany().HasForeignKey(x => new { x.AcademyId, x.BranchId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TrainingGroup>().WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingGroupId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FacilityResource>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ResourceId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.ApprovedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.PostedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AcademyId, x.CreatedByUserId, x.RequestKey }).IsUnique();
            e.HasIndex(x => new { x.AcademyId, x.PeriodFrom, x.PeriodTo, x.Status });
            e.ToTable(t => t.HasCheckConstraint("CK_OperatingExpense_Terms", "\"Amount\">0 AND \"PeriodFrom\"<=\"PeriodTo\" AND \"Status\" BETWEEN 1 AND 4 AND ((\"Scope\"=1 AND \"TrainingGroupId\" IS NOT NULL AND \"ResourceId\" IS NULL) OR (\"Scope\"=2 AND \"ResourceId\" IS NOT NULL AND \"TrainingGroupId\" IS NULL))"));
        });
        b.Entity<OperatingExpenseAllocation>(e => {
            e.Property(x => x.Amount).HasPrecision(18, 2);
            e.Property(x => x.Currency).HasMaxLength(3);
            e.Property(x => x.ServiceKind).HasMaxLength(32).HasDefaultValue("Training");
            e.HasOne<OperatingExpense>().WithMany().HasForeignKey(x => new { x.AcademyId, x.ExpenseId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<TrainingGroup>().WithMany().HasForeignKey(x => new { x.AcademyId, x.TrainingGroupId }).HasPrincipalKey(x => new { x.AcademyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.AcademyId, x.ExpenseId, x.ServiceKind, x.TrainingGroupId }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_OperatingExpenseAllocation_Terms", "\"Amount\">=0 AND \"Weight\">=0"));
        });
    }
}
