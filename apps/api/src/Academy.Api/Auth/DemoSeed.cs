using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Academy.Api.Auth;

public static class DemoSeed
{
    public static readonly Guid NogoomAcademyId = Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static readonly Guid FutureAcademyId = Guid.Parse("20000000-0000-0000-0000-000000000002");
    public const string OwnerEmail = "owner.nogoom@example.test";
    public const string AdminEmail = "admin.nogoom@example.test";
    public const string CoachEmail = "coach.nogoom@example.test";
    public const string GuardianPhone = "+201000000001";
    public const string FutureOwnerEmail = "owner.future@example.test";

    public static void ValidateEnvironment(IHostEnvironment environment, DemoOptions options)
    {
        if ((options.SeedEnabled || options.FixedOtpEnabled) && !environment.IsEnvironment("Demo"))
        {
            throw new InvalidOperationException("Demo seed and fixed OTP are permitted only when ASPNETCORE_ENVIRONMENT=Demo.");
        }

        if (options.FixedOtpEnabled && (options.FixedOtp.Length != 6 || options.FixedOtp.Any(x => !char.IsDigit(x))))
        {
            throw new InvalidOperationException("Demo:FixedOtp must contain exactly six digits.");
        }
    }

    public static async Task SeedAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        await using var scope = services.CreateAsyncScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<DemoOptions>>().Value;
        var environment = scope.ServiceProvider.GetRequiredService<IHostEnvironment>();
        ValidateEnvironment(environment, options);
        if (!options.SeedEnabled) return;
        if (options.StaffPassword.Length < 12) throw new InvalidOperationException("Demo:StaffPassword must be at least 12 characters.");

        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var now = DateTimeOffset.Parse("2026-09-28T06:00:00Z");

        await UpsertAcademy(db, new Infrastructure.Tenancy.Academy
        {
            Id = NogoomAcademyId, ArabicName = "أكاديمية النجوم الرياضية", EnglishName = "Nogoom Sports Academy",
            Slug = "nogoom-demo", TimeZone = "Africa/Cairo", DefaultCurrency = "EGP", CreatedAtUtc = now, UpdatedAtUtc = now
        }, cancellationToken);
        await UpsertAcademy(db, new Infrastructure.Tenancy.Academy
        {
            Id = FutureAcademyId, ArabicName = "أكاديمية المستقبل الرياضية", EnglishName = "Future Sports Academy",
            Slug = "future-demo", TimeZone = "Africa/Cairo", DefaultCurrency = "EGP", CreatedAtUtc = now, UpdatedAtUtc = now
        }, cancellationToken);

        var owner = await UpsertUser(users, OwnerEmail, null, "أحمد محمد", options.StaffPassword, now);
        var admin = await UpsertUser(users, AdminEmail, null, "منى السيد", options.StaffPassword, now);
        var coach = await UpsertUser(users, CoachEmail, null, "كريم حسن", options.StaffPassword, now);
        var guardian = await UpsertUser(users, null, GuardianPhone, "سارة محمود", null, now);
        var futureOwner = await UpsertUser(users, FutureOwnerEmail, null, "محمود علي", options.StaffPassword, now);

        await UpsertMembership(db, NogoomAcademyId, owner.Id, AcademyRole.AcademyOwner, now, cancellationToken);
        await UpsertMembership(db, NogoomAcademyId, admin.Id, AcademyRole.AcademyAdmin, now, cancellationToken);
        await UpsertMembership(db, NogoomAcademyId, coach.Id, AcademyRole.Coach, now, cancellationToken);
        await UpsertMembership(db, NogoomAcademyId, guardian.Id, AcademyRole.Guardian, now, cancellationToken);
        await UpsertMembership(db, FutureAcademyId, futureOwner.Id, AcademyRole.AcademyOwner, now, cancellationToken);
        await Slice2DemoSeed.SeedAsync(db, users, owner.Id, futureOwner.Id, guardian.Id, coach.Id, now, cancellationToken);
        await Slice3DemoSeed.SeedAsync(db, guardian.Id, now, cancellationToken);
    }

    private static async Task UpsertAcademy(FoundationDbContext db, Infrastructure.Tenancy.Academy academy, CancellationToken ct)
    {
        if (!await db.Academies.AnyAsync(x => x.Id == academy.Id, ct))
        {
            db.Academies.Add(academy);
            await db.SaveChangesAsync(ct);
        }
    }

    private static async Task<ApplicationUser> UpsertUser(UserManager<ApplicationUser> manager, string? email, string? phone, string name, string? password, DateTimeOffset now)
    {
        ApplicationUser? user = email is not null ? await manager.FindByEmailAsync(email) : manager.Users.SingleOrDefault(x => x.PhoneNumber == phone);
        if (user is not null) return user;
        user = new ApplicationUser
        {
            Id = Guid.NewGuid(), UserName = email ?? phone, Email = email, EmailConfirmed = email is not null,
            PhoneNumber = phone, PhoneNumberConfirmed = phone is not null, DisplayName = name, IsActive = true,
            CreatedAtUtc = now, UpdatedAtUtc = now
        };
        var result = password is null ? await manager.CreateAsync(user) : await manager.CreateAsync(user, password);
        if (!result.Succeeded) throw new InvalidOperationException(string.Join("; ", result.Errors.Select(x => x.Description)));
        return user;
    }

    private static async Task UpsertMembership(FoundationDbContext db, Guid academyId, Guid userId, AcademyRole role, DateTimeOffset now, CancellationToken ct)
    {
        if (await db.AcademyMemberships.AnyAsync(x => x.AcademyId == academyId && x.UserId == userId && x.Role == role, ct)) return;
        db.AcademyMemberships.Add(new AcademyMembership
        {
            Id = Guid.NewGuid(), AcademyId = academyId, UserId = userId, Role = role,
            IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now
        });
        await db.SaveChangesAsync(ct);
    }
}
