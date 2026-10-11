using Academy.Api.Facilities;
using Academy.Infrastructure.People;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
public sealed record GroupTrainingFormatWrite(TrainingRegistrationFormat RegistrationFormat, bool CapacityReviewed);
public sealed partial class GroupPlacementService
{
    // The caller owns the registration/payment transaction. Private admissions use exactly
    // the same operational checks as reviewed placement, never a cheaper legacy bypass.
    public async Task<Academy.Infrastructure.Structure.TrainingGroup> PrepareSpecialAdmission(Guid groupId)
    {
        var (_, group) = await Group(groupId, true);
        return group;
    }
    public async Task FinishSpecialAdmission(Academy.Infrastructure.Structure.TrainingGroup group, Academy.Infrastructure.People.SportEnrollment enrollment)
    {
        var preview = await Inspect(group, [enrollment.Id]);
        var errors = preview.Conflicts.Concat(preview.Players.SelectMany(p => p.Reasons)).Distinct().ToArray();
        if (errors.Length != 0) throw new FacilityRejected(string.Join(" ", errors));
        enrollment.TrainingGroupId = group.Id; enrollment.GroupAssignedFrom = preview.EffectiveDate;
        db.GroupPlacementRevisions.Add(new Academy.Infrastructure.Structure.GroupPlacementRevision { Id = Guid.NewGuid(), AcademyId = group.AcademyId, BranchId = group.BranchId, TrainingGroupId = group.Id, ActorUserId = db.RequestUserId, RequestKey = "admission-" + enrollment.Id.ToString("N"), RequestHash = preview.Fingerprint, Snapshot = System.Text.Json.JsonSerializer.Serialize(preview), EffectiveDate = preview.EffectiveDate, Placed = 1, CreatedAtUtc = time.GetUtcNow(), UpdatedAtUtc = time.GetUtcNow() });
        await db.SaveChangesAsync();
    }
    public async Task<object> TrainingFormat(Guid id, GroupTrainingFormatWrite b)
    {
        if (!Enum.IsDefined(b.RegistrationFormat)) throw new FacilityRejected("نمط التدريب غير صالح.", 422);
        await using var tx = await db.Database.BeginTransactionAsync(); var (_, group) = await Group(id, true);
        if (b.RegistrationFormat != group.RegistrationFormat && (await db.SportEnrollments.AnyAsync(e => e.TrainingGroupId == id && e.IsActive) || await db.EnrollmentGroupTransfers.AnyAsync(e => e.TargetGroupId == id) || await db.EnrollmentSessionMoves.AnyAsync(m => m.TargetGroupId == id))) throw new FacilityRejected("لا تغير نمط مجموعة لها أعضاء أو نقل مؤرخ؛ أنشئ مجموعة بالنمط المطلوب وانقلهم بمراجعة.");
        if (b.RegistrationFormat != TrainingRegistrationFormat.Group && (!b.CapacityReviewed || group.Capacity is null or <= 0)) throw new FacilityRejected("حدد السعة وراجعها صراحة لهذا النمط؛ لا نستنتج السعة من اسم Private أو شبه خاص.", 422);
        group.RegistrationFormat = b.RegistrationFormat; group.FormatReviewedByUserId = b.CapacityReviewed ? db.RequestUserId : null; group.FormatReviewedAtUtc = b.CapacityReviewed ? time.GetUtcNow() : null; group.UpdatedAtUtc = time.GetUtcNow();
        await db.SaveChangesAsync(); await tx.CommitAsync(); return new { group.Id, registrationFormat = group.RegistrationFormat.ToString(), group.FormatReviewedAtUtc };
    }
}
