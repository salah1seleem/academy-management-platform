using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Api.Slice8;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Importing;

public sealed record ImportGuardianResolution(string HouseholdKey, Guid GuardianId);
public sealed record ImportGuardianMatch(string HouseholdKey, string GuardianName, string Phone, Guid[] Candidates);
public sealed class ImportCommitService(FoundationDbContext db, UserManager<ApplicationUser> users, TimeProvider time, ISubscriptionClock clock)
{
    public static string Hash(object value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    public static string FamilyKey(ImportPreviewRow r) => r.HouseholdRef is null ? "P:" + r.LegacyPlayerId : "H:" + r.HouseholdRef;
    public static string PlayerHash(ImportPreviewRow r) => Hash(new { r.PlayerName, r.BirthDate, phone = EgyptPhoneNormalizer.Normalize(r.GuardianPhone), r.GuardianName, family = FamilyKey(r) });
    public static string? Source(ImportWorkbook book) => book.Sheet("Metadata").Rows.Skip(1).FirstOrDefault(r => r.Count >= 2 && r[0].Value == "SourceSystem")?[1].Value;
    public static async Task<HashSet<string>> ExistingIds(FoundationDbContext db, Guid academy, ImportWorkbook book) => (await db.ImportedEnrollments.Where(x => x.AcademyId == academy && x.SourceSystem == Source(book)).Select(x => x.LegacyEnrollmentId).ToListAsync()).ToHashSet(StringComparer.Ordinal);
    public static async Task<ImportGuardianMatch[]> Matches(FoundationDbContext db, Guid academy, ImportPreviewResult preview)
    {
        var known = await db.ImportedHouseholds.Where(h => h.AcademyId == academy && h.SourceSystem == preview.SourceSystem).Select(h => h.HouseholdKey).ToArrayAsync();
        var families = preview.Rows.DistinctBy(FamilyKey).Where(r => !known.Contains(FamilyKey(r))).ToArray(); var phones = families.Select(r => EgyptPhoneNormalizer.Normalize(r.GuardianPhone)).Where(p => p is not null).ToArray();
        var profiles = await db.GuardianProfiles.AsNoTracking().Where(g => g.AcademyId == academy && g.IsActive && g.User.IsActive && phones.Contains(g.User.PhoneNumber)).Select(g => new { g.Id, g.DisplayName, g.User.PhoneNumber }).ToListAsync();
        // Reveal only that manual reconciliation is needed, never a profile from another branch/tenant.
        var occupied = await db.Users.Where(u => phones.Contains(u.PhoneNumber) || phones.Contains(u.UserName)).Select(u => u.PhoneNumber ?? u.UserName).ToArrayAsync();
        return families.Where(r => occupied.Contains(EgyptPhoneNormalizer.Normalize(r.GuardianPhone)))
            .Select(r => new ImportGuardianMatch(FamilyKey(r), r.GuardianName, r.GuardianPhone, profiles.Where(p => p.PhoneNumber == EgyptPhoneNormalizer.Normalize(r.GuardianPhone) && p.DisplayName == r.GuardianName).Select(p => p.Id).ToArray())).ToArray();
    }
    public async Task Publish(Guid id, CancellationToken token = default)
    {
        // All publication and completion use ONE transaction; nothing is visible midway.
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var job = await db.AcademyImportJobs.FromSqlInterpolated($"SELECT *, xmin FROM \"AcademyImportJobs\" WHERE \"Id\" = {id} FOR UPDATE").SingleAsync(token);
        if (job.State == ImportJobState.Completed) return;
        if (job.State != ImportJobState.Running || job.StagedFile is null || job.ExpiresAtUtc <= time.GetUtcNow()) throw new ImportRejected("expired");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{job.AcademyId}:{job.MembershipId}"}, 0))", token);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:" + job.AcademyId}, 0))", token);
        var branches = await ImportAuthority.Check(db, job.AcademyId, job.ActorUserId, job.MembershipId, job.OpeningBalancesApproved, bind: true);
        var approved = JsonSerializer.Deserialize<Guid[]>(job.ApprovedBranches)!;
        if (branches is not null && approved.Any(b => !branches.Contains(b))) throw new ImportRejected("authority");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"import-source:{job.AcademyId}:{job.SourceSystem}"}, 0))", token);
        var book = ImportXlsx.Read(job.StagedFile);
        if (Convert.ToHexString(SHA256.HashData(job.StagedFile)) != job.FileHash || Source(book) != job.SourceSystem) throw new ImportRejected("validation");
        var mappings = await db.ImportedEnrollments.IgnoreQueryFilters().Where(x => x.AcademyId == job.AcademyId && x.SourceSystem == job.SourceSystem).ToListAsync(token);
        var initially = ImportPreview.Validate(book, await ImportReferences.Load(db, job.AcademyId, time), job.FileHash, mappings.Select(m => m.LegacyEnrollmentId).ToHashSet());
        if (!initially.CanApprove || initially.ReferenceStamp != job.ReferenceStamp) throw new ImportRejected("stale");
        foreach (var group in initially.Rows.Select(r => r.GroupId).Distinct().Order())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"group-capacity:{job.AcademyId}:{group}"}, 0))", token);
            await db.TrainingGroups.FromSqlInterpolated($"SELECT *, xmin FROM \"TrainingGroups\" WHERE \"AcademyId\" = {job.AcademyId} AND \"Id\" = {group} FOR UPDATE").SingleAsync(token);
        }
        foreach (var plan in initially.Rows.Select(r => r.ProductId).Distinct().Order()) await PlanCatalog.LockAsync(db, job.AcademyId, plan);
        var preview = ImportPreview.Validate(book, await ImportReferences.Load(db, job.AcademyId, time), job.FileHash, mappings.Select(m => m.LegacyEnrollmentId).ToHashSet());
        if (!preview.CanApprove || preview.ReferenceStamp != job.ReferenceStamp) throw new ImportRejected("stale");
        if (preview.Rows.Any(r => !approved.Contains(r.BranchId))) throw new ImportRejected("authority");
        var balances = Balances(book); if (balances.Length > 0 && !job.OpeningBalancesApproved) throw new ImportRejected("authority");
        var resolutions = JsonSerializer.Deserialize<ImportGuardianResolution[]>(job.GuardianResolutions)!;
        var families = await db.ImportedHouseholds.IgnoreQueryFilters().Where(x => x.AcademyId == job.AcademyId && x.SourceSystem == job.SourceSystem).ToDictionaryAsync(x => x.HouseholdKey, token);
        var guardians = new Dictionary<string, GuardianProfile>(StringComparer.Ordinal); var players = new Dictionary<string, Player>(StringComparer.Ordinal);
        var plans = await db.SubscriptionPlans.Where(p => preview.Rows.Select(r => r.ProductId).Contains(p.Id)).ToDictionaryAsync(p => p.Id, token);
        var now = time.GetUtcNow(); var replayed = 0; var published = 0;
        // A shared global phone is not a family decision. Existing accounts require a scoped explicit profile selection.
        foreach (var row in preview.Rows.DistinctBy(FamilyKey).OrderBy(r => EgyptPhoneNormalizer.Normalize(r.GuardianPhone), StringComparer.Ordinal))
        {
            var key = FamilyKey(row); var phone = EgyptPhoneNormalizer.Normalize(row.GuardianPhone) ?? throw new ImportRejected("identity");
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"import-phone:" + phone}, 0))", token);
            var identity = Hash(new { row.GuardianName, phone });
            GuardianProfile guardian;
            if (families.TryGetValue(key, out var family))
            {
                if (family.IdentityHash != identity) throw new ImportRejected("identity");
                guardian = await db.GuardianProfiles.SingleOrDefaultAsync(g => g.Id == family.GuardianId && g.IsActive, token) ?? throw new ImportRejected("authority");
            }
            else
            {
                var resolution = resolutions.SingleOrDefault(r => r.HouseholdKey == key);
                if (resolution is not null)
                    guardian = await db.GuardianProfiles.SingleOrDefaultAsync(g => g.Id == resolution.GuardianId && g.AcademyId == job.AcademyId && g.IsActive && g.DisplayName == row.GuardianName && g.User.IsActive && g.User.PhoneNumber == phone, token) ?? throw new ImportRejected("identity");
                else
                {
                    var normalized = users.NormalizeName(phone);
                    if (await db.Users.AnyAsync(u => u.PhoneNumber == phone || u.NormalizedUserName == normalized, token)) throw new ImportRejected("identity");
                    // Bulk passwordless provisioning uses Identity normalization/stamps. No password,
                    // confirmed phone, session, OTP or message is created by importing a contact.
                    var user = new ApplicationUser { Id = Guid.NewGuid(), UserName = phone, NormalizedUserName = normalized, PhoneNumber = phone, PhoneNumberConfirmed = false, DisplayName = row.GuardianName, SecurityStamp = Guid.NewGuid().ToString("N"), ConcurrencyStamp = Guid.NewGuid().ToString("N"), LockoutEnabled = true, CreatedAtUtc = now, UpdatedAtUtc = now };
                    db.Users.Add(user);
                    db.AcademyMemberships.Add(new() { Id = Guid.NewGuid(), AcademyId = job.AcademyId, UserId = user.Id, Role = AcademyRole.Guardian, CreatedAtUtc = now, UpdatedAtUtc = now });
                    guardian = new() { Id = Guid.NewGuid(), AcademyId = job.AcademyId, UserId = user.Id, DisplayName = row.GuardianName, ContactPhone = phone, CreatedAtUtc = now, UpdatedAtUtc = now };
                    db.GuardianProfiles.Add(guardian);
                }
                db.ImportedHouseholds.Add(new() { Id = Guid.NewGuid(), AcademyId = job.AcademyId, SourceSystem = job.SourceSystem, HouseholdKey = key, IdentityHash = identity, GuardianId = guardian.Id, JobId = job.Id, CreatedAtUtc = now, UpdatedAtUtc = now });
            }
            guardians[key] = guardian;
        }
        foreach (var row in preview.Rows)
        {
            token.ThrowIfCancellationRequested();
            var identity = PlayerHash(row); var rowBalances = balances.Where(b => b.LegacyEnrollmentId == row.LegacyEnrollmentId).OrderBy(b => b.Reference).ToArray();
            var rowHash = Hash(new { row = row with { Row = 0, PriceRevision = 0 }, cutoff = job.CutoffDate, balances = rowBalances });
            var existing = mappings.SingleOrDefault(m => m.LegacyEnrollmentId == row.LegacyEnrollmentId);
            if (existing is not null)
            {
                if (existing.RowHash != rowHash || branches is not null && !branches.Contains(existing.BranchId)) throw new ImportRejected("conflict");
                replayed++; continue;
            }
            var mapped = mappings.FirstOrDefault(m => m.LegacyPlayerId == row.LegacyPlayerId);
            if (!players.TryGetValue(row.LegacyPlayerId, out var player))
            {
                if (mapped is not null)
                {
                    if (mapped.PlayerIdentityHash != identity || branches is not null && !branches.Contains(mapped.BranchId)) throw new ImportRejected("identity");
                    player = await db.Players.SingleOrDefaultAsync(p => p.Id == mapped.PlayerId && p.IsActive, token) ?? throw new ImportRejected("authority");
                }
                else
                {
                    var playerId = Guid.NewGuid();
                    player = new() { Id = playerId, AcademyId = job.AcademyId, PlayerCode = "IMP-" + playerId.ToString("N")[..20], ArabicName = row.PlayerName, DateOfBirth = row.BirthDate, CreatedAtUtc = now, UpdatedAtUtc = now };
                    db.Players.Add(player);
                    db.GuardianPlayerLinks.Add(new() { Id = Guid.NewGuid(), AcademyId = job.AcademyId, GuardianId = guardians[FamilyKey(row)].Id, PlayerId = player.Id, CreatedByUserId = job.ActorUserId, RelationshipType = "ولي أمر — اعتماد استيراد", CreatedAtUtc = now, UpdatedAtUtc = now });
                }
                players[row.LegacyPlayerId] = player;
            }
            if (await db.SportEnrollments.AnyAsync(e => e.AcademyId == job.AcademyId && e.PlayerId == player.Id && e.SportId == row.SportId, token)) throw new ImportRejected("conflict");
            var enrollment = new SportEnrollment { Id = Guid.NewGuid(), AcademyId = job.AcademyId, PlayerId = player.Id, BranchId = row.BranchId, SportId = row.SportId, TrainingGroupId = row.GroupId, Status = EnrollmentStatus.Active, CreatedAtUtc = now, UpdatedAtUtc = now };
            var plan = plans[row.ProductId]; var period = new SubscriptionPeriod { Id = Guid.NewGuid(), AcademyId = job.AcademyId, SportEnrollmentId = enrollment.Id, SportId = row.SportId, SubscriptionPlanId = plan.Id, CollectionId = null, Origin = SubscriptionOrigin.ImportedHistorical, ImportJobId = job.Id,
                StartDate = row.Start, EndDate = row.End, InitialSessions = row.EntitledSessions, RemainingSessions = row.RemainingSessions, PriceSnapshot = row.AgreedNet is null ? null : ImportPreview.Amount(row.AgreedNet), CurrencySnapshot = row.Currency, CreatedByUserId = job.ActorUserId, CreatedAtUtc = now, UpdatedAtUtc = now,
                PlanTypeSnapshot = plan.PlanType, PlanNameSnapshot = plan.ArabicName, ServiceKindSnapshot = plan.ServiceKind, TrainingFormatSnapshot = plan.TrainingFormat, PlanRevisionSnapshot = row.PriceRevision,
                DurationDaysSnapshot = row.End.HasValue ? row.End.Value.DayNumber - row.Start.DayNumber + 1 : null, SessionCountSnapshot = row.EntitledSessions };
            period.Status = SubscriptionPeriodState.Resolve(SubscriptionPeriodStatus.Active, period.StartDate, period.EndDate, plan.PlanType, period.RemainingSessions, clock.Today);
            db.SportEnrollments.Add(enrollment); db.SubscriptionPeriods.Add(period);
            db.ImportedEnrollments.Add(new() { Id = Guid.NewGuid(), AcademyId = job.AcademyId, JobId = job.Id, SourceSystem = job.SourceSystem, LegacyEnrollmentId = row.LegacyEnrollmentId, LegacyPlayerId = row.LegacyPlayerId, RowHash = rowHash, PlayerIdentityHash = identity, AgreementSnapshot = JsonSerializer.Serialize(new { row, cutoff = job.CutoffDate, openingBalances = rowBalances }), PlayerId = player.Id, GuardianId = guardians[FamilyKey(row)].Id, SportEnrollmentId = enrollment.Id, SubscriptionPeriodId = period.Id, BranchId = row.BranchId, CreatedAtUtc = now, UpdatedAtUtc = now });
            foreach (var balance in rowBalances)
                db.ReceivableDocuments.Add(new() { Id = Guid.NewGuid(), AcademyId = job.AcademyId, ImportJobId = job.Id, SportEnrollmentId = enrollment.Id, PlayerId = player.Id, PlayerNameSnapshot = player.ArabicName, BranchId = row.BranchId, PlanId = plan.Id, PlanNameSnapshot = plan.ArabicName, PlanPriceSnapshot = period.PriceSnapshot, Amount = balance.Amount, Currency = balance.Currency, DueDate = balance.CutoffDate, Source = ReceivableSource.OpeningBalance,
                    SourceKey = "import:" + Hash(new { job.SourceSystem, row.LegacyEnrollmentId, balance.Reference }), ContentHash = Hash(balance), Description = (balance.Amount > 0 ? "رصيد افتتاحي مستحق — " : "رصيد افتتاحي لصالح الأسرة — ") + balance.Reference, ActorUserId = job.ActorUserId, CreatedAtUtc = clock.UtcNow, UpdatedAtUtc = clock.UtcNow });
            published++;
        }
        job.PublishedRows = published; job.ReplayedRows = replayed; job.State = ImportJobState.Completed; job.StagedFile = null; job.ErrorCode = null; job.CompletedAtUtc = now; job.LeaseUntilUtc = null; job.UpdatedAtUtc = now;
        await db.SaveChangesAsync(token); await tx.CommitAsync(token);
    }
    public sealed record Opening(string LegacyEnrollmentId, decimal Amount, string Currency, DateOnly CutoffDate, string Reference, string ApprovalEvidence);
    public static Opening[] Balances(ImportWorkbook book) => book.Sheet("OpenBalances").Rows.Skip(1).Where(r => r.Any(c => c.Value.Length > 0)).Select(r => new Opening(r[0].Value, ImportPreview.Amount(r[1].Value)!.Value, r[2].Value, ImportPreview.Date(r[3].Value)!.Value, r[4].Value, r[5].Value)).ToArray();
}
