using System.Text.Json;
using Academy.Api.Configuration;
using Academy.Api.Facilities;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;

public sealed record GroupMergeWrite(Guid SourceGroupId, Guid TargetGroupId, DateOnly EffectiveDate, string? Fingerprint = null);

public sealed partial class GroupPlacementService
{
    public async Task<object> MergeGroups(GroupMergeWrite body, bool save, string? key = null)
    {
        if (body.SourceGroupId == body.TargetGroupId || body.SourceGroupId == Guid.Empty || body.TargetGroupId == Guid.Empty)
            throw new FacilityRejected("اختر مجموعتين مختلفتين للدمج.", 422);
        if (save && (string.IsNullOrWhiteSpace(key) || key.Length > 64))
            throw new FacilityRejected("معرّف عملية الدمج مطلوب.", 422);

        await using var tx = await db.Database.BeginTransactionAsync();
        var (tenant, source) = await Group(body.SourceGroupId, save);
        var (_, target) = await Group(body.TargetGroupId, save);
        if (tenant.Role != AcademyRole.AcademyOwner) throw new FacilityRejected("دمج المجموعات متاح للمالك.", 403);
        if (source.BranchId != target.BranchId || source.SportId != target.SportId
            || source.RegistrationFormat != target.RegistrationFormat)
            throw new FacilityRejected("اختر مجموعتين بنفس الفرع والرياضة ونمط التدريب.", 422);
        var academy = tenant.AcademyId;
        var action = save ? "Edit" : "Read";
        await Authority("players", action, source.BranchId);
        var requestKey = $"merge:{key}";
        var hash = AcademyConfigurationService.Hash(JsonSerializer.Serialize(new { body.SourceGroupId, body.TargetGroupId, body.EffectiveDate }));
        if (save)
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"transfer-request:{academy}:{db.RequestUserId}:{requestKey}"},0))");
            var prior = await db.EnrollmentGroupTransfers.SingleOrDefaultAsync(x => x.AcademyId == academy && x.ActorUserId == db.RequestUserId && x.RequestKey == requestKey);
            if (prior is not null)
            {
                if (prior.RequestHash != hash) throw new FacilityRejected("معرّف الدمج مستخدم لطلب آخر.");
                return new { prior.Id, replayed = true };
            }
            await coordinator.LockCalendar(academy, [source.Id, target.Id], "Edit");
            (_, source) = await Group(body.SourceGroupId, true);
            (_, target) = await Group(body.TargetGroupId, true);
        }

        var enrollments = await db.SportEnrollments.AsNoTracking().Where(e => e.AcademyId == academy
                && e.IsActive && e.Player.IsActive && e.Status == EnrollmentStatus.Active
                && FoundationDbContext.AssignedGroupAt(academy, e.Id, body.EffectiveDate) == source.Id)
            .OrderBy(e => e.Id).Take(101).ToArrayAsync();
        if (enrollments.Length is 0 or > 100)
            throw new FacilityRejected("يمكن دمج مجموعة من لاعب واحد إلى 100 لاعب في العملية.", 422);
        if (save)
            foreach (var enrollment in enrollments) await LockTransferPlayer(academy, enrollment.Id);

        var ids = enrollments.Select(e => e.Id).ToArray();
        var placement = await Inspect(target, ids, effectiveDate: body.EffectiveDate, transfer: true);
        var transfers = new List<GroupTransferPreview>();
        var conflicts = placement.Conflicts.Concat(placement.Players.SelectMany(p => p.Reasons)).ToList();
        // The shared transfer rules protect past attendance, scheduled transfers,
        // resource reservations and session moves for every member of the batch.
        if (conflicts.Count == 0)
            foreach (var id in ids)
            {
                var transfer = await InspectTransfer(id, new(target.Id, body.EffectiveDate), save);
                transfers.Add(transfer);
                conflicts.AddRange(transfer.Conflicts);
            }
        var fingerprint = AcademyConfigurationService.Hash(JsonSerializer.Serialize(new
        {
            hash, source.Version, placement.Fingerprint, ids, transfers = transfers.Select(x => x.Fingerprint)
        }));
        if (!save) return new { fingerprint, count = ids.Length, source = source.ArabicName, target = target.ArabicName,
            effectiveDate = body.EffectiveDate, players = placement.Players.Select(p => p.Player), conflicts = conflicts.Distinct() };
        if (body.Fingerprint != fingerprint)
            throw new FacilityRejected("تغيرت قائمة اللاعبين أو الجدول؛ أعد معاينة الدمج.");
        if (conflicts.Count > 0) throw new FacilityRejected(string.Join(" ", conflicts.Distinct()));

        var now = time.GetUtcNow();
        var operationId = Guid.NewGuid();
        db.EnrollmentGroupTransfers.AddRange(enrollments.Select((enrollment, index) => new EnrollmentGroupTransfer
        {
            Id = index == 0 ? operationId : Guid.NewGuid(), AcademyId = academy, SportEnrollmentId = enrollment.Id,
            BranchId = enrollment.BranchId, SportId = enrollment.SportId, SourceGroupId = source.Id, TargetGroupId = target.Id,
            EffectiveDate = body.EffectiveDate, ActorUserId = db.RequestUserId,
            RequestKey = index == 0 ? requestKey : $"merge:{operationId:N}:{index}",
            RequestHash = hash, Snapshot = JsonSerializer.Serialize(transfers[index]), CreatedAtUtc = now, UpdatedAtUtc = now
        }));
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new { id = operationId, count = ids.Length, replayed = false };
    }
}
