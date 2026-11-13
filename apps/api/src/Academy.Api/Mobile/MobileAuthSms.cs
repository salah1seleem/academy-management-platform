using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Mobile;

public static partial class MobileAuthEndpoints
{
    // Verify can reuse a code for ten minutes. Keep the reservation longer
    // than that window and the HTTP timeout even when a send times out.
    private static readonly TimeSpan SmsReservation = TimeSpan.FromMinutes(11);

    private static async Task<IResult> StartSmsOtp(string phone, FoundationDbContext db,
        TimeProvider clock, TwilioMobileOtp sms, CancellationToken ct)
    {
        MobileOtpChallenge challenge;
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await LockSmsPhone(db, phone, ct);
            var now = clock.GetUtcNow();
            var recent = db.MobileOtpChallenges.Where(x => x.PhoneNumberNormalized == phone);
            if (await recent.CountAsync(x => x.CreatedAtUtc > now.AddMinutes(-15), ct) >= 3 ||
                await recent.AnyAsync(x => x.ProviderUserId != null && x.ConsumedAtUtc == null &&
                    x.CreatedAtUtc > now - SmsReservation, ct))
                return Results.Problem(statusCode: 429, title: "محاولات كثيرة، يرجى الانتظار قبل طلب رمز جديد");

            var user = await db.Users.SingleOrDefaultAsync(x => x.PhoneNumber == phone && x.IsActive, ct);
            if (user is null || !await Eligible(db, user.Id).AnyAsync(ct)) return Invalid();
            challenge = new MobileOtpChallenge
            {
                Id = Guid.NewGuid(), PhoneNumberNormalized = phone, ProviderUserId = user.Id,
                CreatedAtUtc = now, ExpiresAtUtc = now.AddMinutes(5)
            };
            db.MobileOtpChallenges.Add(challenge);
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }

        var sid = await sms.StartAsync(phone, ct);
        if (sid is null) return SmsTemporarilyUnavailable();
        challenge.ProviderVerificationSid = sid;
        await db.SaveChangesAsync(ct);
        return Results.Accepted(value: new { challengeId = challenge.Id, demo = false });
    }

    private static async Task<IResult> VerifySmsOtp(MobileOtpVerify request, string phone,
        FoundationDbContext db, TimeProvider clock, TwilioMobileOtp sms, CancellationToken ct)
    {
        string sid;
        var candidate = db.MobileOtpChallenges.Where(x => x.Id == request.ChallengeId &&
            x.PhoneNumberNormalized == phone && x.ProviderUserId != null && x.ConsumedAtUtc == null);
        await using (var tx = await db.Database.BeginTransactionAsync(ct))
        {
            await LockSmsPhone(db, phone, ct);
            var now = clock.GetUtcNow();
            var challenge = await candidate.AsNoTracking()
                .SingleOrDefaultAsync(x => x.ExpiresAtUtc > now && x.FailedAttempts < 5, ct);
            if (challenge?.ProviderVerificationSid is not { } verificationSid) return Invalid();
            sid = verificationSid;
            await candidate.ExecuteUpdateAsync(update => update.SetProperty(x => x.FailedAttempts, x => x.FailedAttempts + 1), ct);
            await tx.CommitAsync(ct);
        }

        var check = await sms.CheckAsync(sid, request.Code, ct);
        if (check == MobileOtpCheck.Unavailable) return SmsTemporarilyUnavailable();
        if (check != MobileOtpCheck.Approved) return Invalid();

        await using var finalize = await db.Database.BeginTransactionAsync(ct);
        await LockSmsPhone(db, phone, ct);
        var completedAt = clock.GetUtcNow();
        var approved = await candidate.SingleOrDefaultAsync(x => x.ExpiresAtUtc > completedAt &&
            x.ProviderVerificationSid == sid, ct);
        return approved is null ? Invalid() : await CompleteOtp(db, finalize, approved, request.DeviceName, completedAt);
    }

    private static Task LockSmsPhone(FoundationDbContext db, string phone, CancellationToken ct) =>
        db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"mobile-otp:" + phone}, 0))", ct);

    private static IResult SmsTemporarilyUnavailable() =>
        Results.Problem(statusCode: 503, title: "خدمة رسائل التحقق غير متاحة مؤقتًا. حاول لاحقًا.");
}
