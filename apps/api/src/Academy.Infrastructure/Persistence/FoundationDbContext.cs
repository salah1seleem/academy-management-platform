using Academy.Infrastructure.Identity;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Academy.Infrastructure.Persistence;

public sealed class FoundationDbContext(DbContextOptions<FoundationDbContext> options)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    public DbSet<Tenancy.Academy> Academies => Set<Tenancy.Academy>();
    public DbSet<AcademyMembership> AcademyMemberships => Set<AcademyMembership>();
    public DbSet<UserSession> UserSessions => Set<UserSession>();
    public DbSet<GuardianOtpChallenge> GuardianOtpChallenges => Set<GuardianOtpChallenge>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.ToTable("Users");
            entity.Property(x => x.DisplayName).HasMaxLength(160);
            entity.Property(x => x.PhoneNumber).HasMaxLength(32);
            entity.Property(x => x.Email).HasMaxLength(256);
            entity.HasIndex(x => x.NormalizedEmail).IsUnique();
            entity.HasIndex(x => x.PhoneNumber).IsUnique();
        });

        builder.Ignore<IdentityUserLogin<Guid>>();
        builder.Ignore<IdentityUserToken<Guid>>();

        builder.Entity<Tenancy.Academy>(entity =>
        {
            entity.ToTable("Academies");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ArabicName).HasMaxLength(200);
            entity.Property(x => x.EnglishName).HasMaxLength(200);
            entity.Property(x => x.Slug).HasMaxLength(100);
            entity.Property(x => x.TimeZone).HasMaxLength(80);
            entity.Property(x => x.DefaultCurrency).HasMaxLength(3).IsFixedLength();
            entity.Property(x => x.Version).IsRowVersion();
            entity.HasIndex(x => x.Slug).IsUnique();
        });

        builder.Entity<AcademyMembership>(entity =>
        {
            entity.ToTable("AcademyMemberships");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Role).HasConversion<string>().HasMaxLength(32);
            entity.HasIndex(x => new { x.AcademyId, x.UserId }).IsUnique();
            entity.HasIndex(x => new { x.UserId, x.IsActive });
            entity.HasOne(x => x.Academy).WithMany().HasForeignKey(x => x.AcademyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<UserSession>(entity =>
        {
            entity.ToTable("UserSessions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasMaxLength(128);
            entity.HasIndex(x => new { x.UserId, x.RevokedAtUtc });
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<GuardianOtpChallenge>(entity =>
        {
            entity.ToTable("GuardianOtpChallenges");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PhoneNumberNormalized).HasMaxLength(32);
            entity.HasIndex(x => new { x.PhoneNumberNormalized, x.ExpiresAtUtc });
        });
    }
}
