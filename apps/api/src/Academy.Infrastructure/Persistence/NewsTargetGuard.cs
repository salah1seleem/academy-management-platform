using Academy.Infrastructure.Operations;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed partial class FoundationDbContext
{
    private async Task GuardNewsTargets(CancellationToken token)
    {
        foreach (var entry in ChangeTracker.Entries<AcademyNews>().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            var row = entry.Entity;
            if(row.StartsAtUtc.HasValue!=row.EndsAtUtc.HasValue||row.StartsAtUtc>=row.EndsAtUtc||row.StartsAtUtc.HasValue&&row.EndsAtUtc-row.StartsAtUtc>TimeSpan.FromDays(90))
                throw new InvalidOperationException("فترة نشر المحتوى غير صحيحة.");
            if (row.TargetBranchIds.Length > 100 || row.TargetBranchIds.Distinct().Count() != row.TargetBranchIds.Length ||
                await Branches.IgnoreQueryFilters().CountAsync(b => b.AcademyId == row.AcademyId && row.TargetBranchIds.Contains(b.Id), token) != row.TargetBranchIds.Length)
                throw new ResourceScopeException();
            if (RequestScopeEnabled && BranchScopeRestricted && (row.TargetBranchIds.Length == 0 || row.TargetBranchIds.Any(b => !RequestBranchIds.Contains(b)) ||
                entry.State == EntityState.Modified && (entry.OriginalValues.GetValue<Guid[]>(nameof(Academy.Infrastructure.Operations.AcademyNews.TargetBranchIds)).Length == 0 ||
                entry.OriginalValues.GetValue<Guid[]>(nameof(Academy.Infrastructure.Operations.AcademyNews.TargetBranchIds)).Any(b => !RequestBranchIds.Contains(b)))))
                throw new ResourceScopeException();
            if (entry.State == EntityState.Modified && (entry.Property(nameof(Academy.Infrastructure.Operations.AcademyNews.CreationRequestId)).IsModified || entry.Property(nameof(Academy.Infrastructure.Operations.AcademyNews.CreationHash)).IsModified))
                throw new InvalidOperationException("هوية إنشاء المحتوى وبصمتها محفوظتان.");
        }
    }
}
