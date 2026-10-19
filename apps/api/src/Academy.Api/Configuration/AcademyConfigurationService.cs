using System.Security.Cryptography;
using System.Text;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Configuration;

public sealed class AcademyConfigurationService(FoundationDbContext db, TimeProvider clock)
{
    // Deliberately no cross-request cache: revocation/downgrade is effective on
    // the next authorized call. Any future cache must key academy + revision.
    private readonly Dictionary<Guid, AcademyRuntimeSettings?> requestSettings = [];
    private readonly Dictionary<Guid, Dictionary<string, bool>> requestLicenses = [];
    // A write that acquires the configuration lock must not reuse a pre-lock read.
    public void DiscardRequestCache(Guid academyId)
    {
        requestSettings.Remove(academyId);
        requestLicenses.Remove(academyId);
    }
    public async Task<AcademyRuntimeSettings?> Active(Guid academyId)
    {
        if (requestSettings.TryGetValue(academyId, out var stored)) return stored;
        var v = await db.AcademyConfigurationVersions.AsNoTracking().SingleOrDefaultAsync(x => x.AcademyId == academyId && x.State == ConfigurationState.Active);
        return requestSettings[academyId] = v is null ? null : AcademyRuntimeSettings.Read(v.Payload);
    }

    public async Task<bool> Enabled(Guid academyId, string feature)
    {
        if (!AcademyFeatures.All.Contains(feature)) return false;
        var settings = await Active(academyId);
        if (settings?.Features.GetValueOrDefault(feature) != true) return false;
        if (!requestLicenses.TryGetValue(academyId, out var licenses))
            requestLicenses[academyId] = licenses = await db.AcademyFeatureEntitlements.AsNoTracking().Where(x => x.AcademyId == academyId && x.IsActive).ToDictionaryAsync(x => x.Feature, x => x.Enabled);
        return licenses.GetValueOrDefault(feature);
    }

    public static string Hash(string payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));

    public async Task<AcademyConfigurationVersion> CreateDraft(Guid academyId, Guid actor, AcademyRuntimeSettings settings)
    {
        var payload = settings.Serialize(); var now = clock.GetUtcNow();
        var hash = Hash(payload);
        var repeated = await db.AcademyConfigurationVersions.Where(x => x.AcademyId == academyId && x.State == ConfigurationState.Draft && x.CreatedByUserId == actor && x.ContentHash == hash).OrderByDescending(x => x.Revision).FirstOrDefaultAsync();
        if (repeated is not null) return repeated;
        var next = (await db.AcademyConfigurationVersions.Where(x => x.AcademyId == academyId).MaxAsync(x => (int?)x.Revision) ?? 0) + 1;
        var row = new AcademyConfigurationVersion
        {
            Id = Guid.NewGuid(),
            AcademyId = academyId,
            Revision = next,
            Payload = payload,
            ContentHash = hash,
            CreatedByUserId = actor,
            State = ConfigurationState.Draft,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        db.AcademyConfigurationVersions.Add(row); await db.SaveChangesAsync(); return row;
    }

    public async Task<string[]> Validate(AcademyConfigurationVersion row)
    {
        var a = await db.Academies.AsNoTracking().SingleAsync(x => x.Id == row.AcademyId);
        var licenses = await db.AcademyFeatureEntitlements.Where(x => x.AcademyId == row.AcademyId && x.IsActive).ToDictionaryAsync(x => x.Feature, x => x.Enabled);
        var settings = AcademyRuntimeSettings.Read(row.Payload);
        return [.. settings.Validate(licenses, a.DefaultCurrency, a.TimeZone), .. Academy.Api.Help.HelpAudiencePolicy.Validate(settings.HelpAudiences)];
    }

    public async Task Transition(AcademyConfigurationVersion row, ConfigurationState target, Guid actor)
    {
        var now = clock.GetUtcNow();
        db.ConfigurationTransitions.Add(new ConfigurationTransition
        {
            Id = Guid.NewGuid(),
            AcademyId = row.AcademyId,
            ConfigurationId = row.Id,
            ActorUserId = actor,
            From = row.State,
            To = target,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        });
        row.State = target; row.UpdatedAtUtc = now; await db.SaveChangesAsync();
    }

    // Only called for explicit synthetic Demo academies, never all customer rows.
    public static async Task SeedDemo(FoundationDbContext db, IEnumerable<Guid> academyIds)
    {
        var now = DateTimeOffset.Parse("2026-09-28T06:00:00Z");
        foreach (var id in academyIds.Distinct())
        {
            var academy = await db.Academies.SingleAsync(x => x.Id == id);
            if (await db.AcademyConfigurationVersions.AnyAsync(x => x.AcademyId == id)) continue;
            var settings = AcademyRuntimeSettings.Initial(academy.DefaultCurrency, academy.TimeZone, preserveExisting: true); var payload = settings.Serialize();
            db.AcademyConfigurationVersions.Add(new()
            {
                Id = Guid.NewGuid(),
                AcademyId = id,
                Revision = 1,
                State = ConfigurationState.Active,
                Payload = payload,
                ContentHash = Hash(payload),
                EffectiveAtUtc = now,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
            foreach (var f in AcademyFeatures.All) db.AcademyFeatureEntitlements.Add(new()
            {
                Id = Guid.NewGuid(),
                AcademyId = id,
                Feature = f,
                Enabled = AcademyFeatures.Existing.Contains(f),
                PlanCode = "LegacyDemo",
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }
        await db.SaveChangesAsync();
    }
}
