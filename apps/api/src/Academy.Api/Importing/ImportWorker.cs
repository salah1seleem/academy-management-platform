using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Importing;

// Durable bounded DB queue, no external broker. Expired Running leases are reclaimed
// after a process restart; the publication transaction either commits everything or nothing.
public sealed class ImportWorker(IServiceScopeFactory scopes, TimeProvider time, ILogger<ImportWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var id = await Claim(scopes, time, stoppingToken);
                if (id.HasValue)
                {
                    try { await using var scope = scopes.CreateAsyncScope(); await scope.ServiceProvider.GetRequiredService<ImportCommitService>().Publish(id.Value, stoppingToken); }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; } // lease recovers, transaction rolled back
                    catch (Exception error)
                    {
                        var code = error is ImportRejected r ? r.Code : "storage";
                        // Deliberately do not log workbook values or database exception detail (PII).
                        logger.LogWarning("Import job {JobId} did not publish; code {Code}", id, code);
                        await Fail(scopes, id.Value, code, time.GetUtcNow(), stoppingToken);
                    }
                    continue;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception) { logger.LogWarning("Import queue unavailable; waiting for the next bounded check"); }
            try { if (!await timer.WaitForNextTickAsync(stoppingToken)) break; } catch (OperationCanceledException) { break; }
        }
    }
    public static async Task<Guid?> Claim(IServiceScopeFactory scopes, TimeProvider time, CancellationToken token = default)
    {
        await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>(); var now = time.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(token);
        var job = await db.AcademyImportJobs.FromSqlInterpolated($"SELECT *, xmin FROM \"AcademyImportJobs\" WHERE (\"State\" = 'Queued' OR (\"State\" = 'Running' AND \"LeaseUntilUtc\" < {now})) ORDER BY \"CreatedAtUtc\", \"Id\" LIMIT 1 FOR UPDATE SKIP LOCKED").SingleOrDefaultAsync(token);
        if (job is null) return null;
        if (job.ExpiresAtUtc <= now || job.Attempts >= 3) { job.State = ImportJobState.Expired; job.StagedFile = null; job.ErrorCode = "expired"; }
        else { job.State = ImportJobState.Running; job.Attempts++; job.LeaseUntilUtc = now.AddMinutes(10); }
        job.UpdatedAtUtc = now; await db.SaveChangesAsync(token); await tx.CommitAsync(token);
        return job.State == ImportJobState.Running ? job.Id : null;
    }
    public static async Task Fail(IServiceScopeFactory scopes, Guid id, string code, DateTimeOffset now, CancellationToken token = default)
    {
        await using var scope = scopes.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var job = await db.AcademyImportJobs.SingleAsync(j => j.Id == id, token);
        if (job.State != ImportJobState.Running) return;
        job.State = code == "storage" && job.Attempts < 3 && job.ExpiresAtUtc > now ? ImportJobState.Queued : ImportJobState.Failed;
        job.ErrorCode = code; job.LeaseUntilUtc = null; job.UpdatedAtUtc = now;
        if (job.State == ImportJobState.Failed) job.StagedFile = null;
        await db.SaveChangesAsync(token);
    }
}
