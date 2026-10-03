using Academy.Api.Auth;
using Academy.Infrastructure.Identity;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Structure;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice2;

public static class Slice2DemoSeed
{
    public static readonly Guid CityBranchId = Guid.Parse("31000000-0000-0000-0000-000000000001");
    public static readonly Guid FifthBranchId = Guid.Parse("31000000-0000-0000-0000-000000000002");
    public static readonly Guid ZagazigBranchId = Guid.Parse("31000000-0000-0000-0000-000000000003");
    public static readonly Guid FootballId = Guid.Parse("32000000-0000-0000-0000-000000000001");
    public static readonly Guid SwimmingId = Guid.Parse("32000000-0000-0000-0000-000000000002");
    public static readonly Guid BasketballId = Guid.Parse("32000000-0000-0000-0000-000000000003");
    public static readonly Guid U8Id = Guid.Parse("33000000-0000-0000-0000-000000000001");
    public static readonly Guid U10Id = Guid.Parse("33000000-0000-0000-0000-000000000002");
    public static readonly Guid U12Id = Guid.Parse("33000000-0000-0000-0000-000000000003");
    public static readonly Guid FootballGroupId = Guid.Parse("34000000-0000-0000-0000-000000000001");
    public static readonly Guid SwimmingGroupId = Guid.Parse("34000000-0000-0000-0000-000000000002");
    public static readonly Guid JuniorGroupId = Guid.Parse("34000000-0000-0000-0000-000000000003");
    public static readonly Guid MainGuardianId = Guid.Parse("35000000-0000-0000-0000-000000000001");
    public static readonly Guid OmarPlayerId = Guid.Parse("36000000-0000-0000-0000-000000000001");
    public static readonly Guid MariamPlayerId = Guid.Parse("36000000-0000-0000-0000-000000000002");
    public static readonly Guid OtherPlayerId = Guid.Parse("36000000-0000-0000-0000-000000000003");
    public static readonly Guid AcademyBPlayerId = Guid.Parse("36000000-0000-0000-0000-000000000004");

    public static async Task SeedAsync(FoundationDbContext db, UserManager<ApplicationUser> users, Guid ownerId, Guid futureOwnerId, Guid guardianUserId, Guid coachUserId, DateTimeOffset now, CancellationToken ct)
    {
        await AddIfMissing(db, new Branch { Id = CityBranchId, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "فرع مدينة نصر", Address = "مدينة نصر — بيانات تجريبية", CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        await AddIfMissing(db, new Branch { Id = FifthBranchId, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "فرع التجمع الخامس", CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        await AddIfMissing(db, new Branch { Id = ZagazigBranchId, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "فرع الزقازيق", CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        await AddIfMissing(db, new Sport { Id = FootballId, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "كرة القدم", CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        await AddIfMissing(db, new Sport { Id = SwimmingId, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "السباحة", CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        await AddIfMissing(db, new Sport { Id = BasketballId, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "كرة السلة", CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        await AddIfMissing(db, new AgeCategory { Id = U8Id, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "تحت 8 سنوات", MinimumBirthYear = 2018, MaximumBirthYear = 2020, CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        await AddIfMissing(db, new AgeCategory { Id = U10Id, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "تحت 10 سنوات", MinimumBirthYear = 2016, MaximumBirthYear = 2017, CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        await AddIfMissing(db, new AgeCategory { Id = U12Id, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "تحت 12 سنة", MinimumBirthYear = 2014, MaximumBirthYear = 2015, CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        await AddIfMissing(db, new TrainingGroup { Id = FootballGroupId, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "ناشئين 2016 — مجموعة أ", BranchId = CityBranchId, SportId = FootballId, AgeCategoryId = U10Id, Capacity = 24, CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        await AddIfMissing(db, new TrainingGroup { Id = SwimmingGroupId, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "سباحة ناشئين — مجموعة أ", BranchId = FifthBranchId, SportId = SwimmingId, AgeCategoryId = U10Id, Capacity = 18, CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        await AddIfMissing(db, new TrainingGroup { Id = JuniorGroupId, AcademyId = DemoSeed.NogoomAcademyId, ArabicName = "براعم 2018 — مجموعة أ", BranchId = ZagazigBranchId, SportId = FootballId, AgeCategoryId = U8Id, Capacity = 20, CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        foreach (var day in new[] { DayOfWeek.Sunday, DayOfWeek.Tuesday, DayOfWeek.Thursday }) await AddIfMissing(db, new RecurringSchedule { Id = Deterministic($"schedule-{day}"), AcademyId = DemoSeed.NogoomAcademyId, TrainingGroupId = FootballGroupId, DayOfWeek = day, StartTime = new TimeOnly(18, 0), EndTime = new TimeOnly(19, 30), CreatedAtUtc = now, UpdatedAtUtc = now }, ct);

        var coachMembership = await db.AcademyMemberships.SingleAsync(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.UserId == coachUserId && x.Role == AcademyRole.Coach, ct);
        await AddIfMissing(db, new StaffGroupAssignment { Id = Guid.Parse("37000000-0000-0000-0000-000000000001"), AcademyId = DemoSeed.NogoomAcademyId, AcademyMembershipId = coachMembership.Id, TrainingGroupId = FootballGroupId, CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        await AddIfMissing(db, new GuardianProfile { Id = MainGuardianId, AcademyId = DemoSeed.NogoomAcademyId, UserId = guardianUserId, DisplayName = "سارة محمود", ContactPhone = DemoSeed.GuardianPhone, CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        await AddIfMissing(db, Player(OmarPlayerId, DemoSeed.NogoomAcademyId, "NG-0001", "عمر أحمد محمود", new DateOnly(2016, 4, 12), now), ct);
        await AddIfMissing(db, Player(MariamPlayerId, DemoSeed.NogoomAcademyId, "NG-0002", "مريم أحمد محمود", new DateOnly(2018, 7, 3), now), ct);
        await Link(db, DemoSeed.NogoomAcademyId, MainGuardianId, OmarPlayerId, ownerId, "ابن", now, ct);
        await Link(db, DemoSeed.NogoomAcademyId, MainGuardianId, MariamPlayerId, ownerId, "ابنة", now, ct);
        await Enroll(db, DemoSeed.NogoomAcademyId, OmarPlayerId, FootballId, CityBranchId, FootballGroupId, now, ct);
        await Enroll(db, DemoSeed.NogoomAcademyId, OmarPlayerId, SwimmingId, FifthBranchId, SwimmingGroupId, now, ct);
        await Enroll(db, DemoSeed.NogoomAcademyId, MariamPlayerId, FootballId, ZagazigBranchId, JuniorGroupId, now, ct);

        var otherUser = await EnsureGuardianUser(users, db, DemoSeed.NogoomAcademyId, "+201000000002", "أحمد محمد علي", now, ct);
        var otherGuardianId = Guid.Parse("35000000-0000-0000-0000-000000000002");
        await AddIfMissing(db, new GuardianProfile { Id = otherGuardianId, AcademyId = DemoSeed.NogoomAcademyId, UserId = otherUser.Id, DisplayName = otherUser.DisplayName, ContactPhone = otherUser.PhoneNumber, CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        await AddIfMissing(db, Player(OtherPlayerId, DemoSeed.NogoomAcademyId, "NG-0003", "عمر أحمد حسن", new DateOnly(2016, 9, 20), now), ct);
        await Link(db, DemoSeed.NogoomAcademyId, otherGuardianId, OtherPlayerId, ownerId, "ابن", now, ct);
        await Enroll(db, DemoSeed.NogoomAcademyId, OtherPlayerId, FootballId, CityBranchId, FootballGroupId, now, ct);

        var bBranch = Guid.Parse("41000000-0000-0000-0000-000000000001"); var bSport = Guid.Parse("42000000-0000-0000-0000-000000000001"); var bCategory = Guid.Parse("43000000-0000-0000-0000-000000000001"); var bGroup = Guid.Parse("44000000-0000-0000-0000-000000000001");
        await AddIfMissing(db, new Branch { Id = bBranch, AcademyId = DemoSeed.FutureAcademyId, ArabicName = "فرع المستقبل", CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        await AddIfMissing(db, new Sport { Id = bSport, AcademyId = DemoSeed.FutureAcademyId, ArabicName = "كرة القدم", CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        await AddIfMissing(db, new AgeCategory { Id = bCategory, AcademyId = DemoSeed.FutureAcademyId, ArabicName = "تحت 10 سنوات", CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        await AddIfMissing(db, new TrainingGroup { Id = bGroup, AcademyId = DemoSeed.FutureAcademyId, ArabicName = "مجموعة المستقبل", BranchId = bBranch, SportId = bSport, AgeCategoryId = bCategory, CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        var bGuardian = await EnsureGuardianUser(users, db, DemoSeed.FutureAcademyId, "+201000000003", "منى إبراهيم", now, ct); var bGuardianId = Guid.Parse("45000000-0000-0000-0000-000000000001");
        await AddIfMissing(db, new GuardianProfile { Id = bGuardianId, AcademyId = DemoSeed.FutureAcademyId, UserId = bGuardian.Id, DisplayName = bGuardian.DisplayName, ContactPhone = bGuardian.PhoneNumber, CreatedAtUtc = now, UpdatedAtUtc = now }, ct);
        await AddIfMissing(db, Player(AcademyBPlayerId, DemoSeed.FutureAcademyId, "FT-0001", "عمر أحمد محمود", new DateOnly(2016, 2, 2), now), ct);
        await Link(db, DemoSeed.FutureAcademyId, bGuardianId, AcademyBPlayerId, futureOwnerId, "ابن", now, ct); await Enroll(db, DemoSeed.FutureAcademyId, AcademyBPlayerId, bSport, bBranch, bGroup, now, ct);
    }

    private static Player Player(Guid id, Guid academy, string code, string name, DateOnly birth, DateTimeOffset now) => new() { Id = id, AcademyId = academy, PlayerCode = code, ArabicName = name, DateOfBirth = birth, Gender = Gender.Male, CreatedAtUtc = now, UpdatedAtUtc = now };
    private static async Task AddIfMissing<TEntity>(FoundationDbContext db, TEntity entity, CancellationToken ct) where TEntity : TenantEntity { if (!await db.Set<TEntity>().AnyAsync(x => x.Id == entity.Id, ct)) { db.Add(entity); await db.SaveChangesAsync(ct); } }
    private static async Task Link(FoundationDbContext db, Guid academy, Guid guardian, Guid player, Guid actor, string relation, DateTimeOffset now, CancellationToken ct) { if (!await db.GuardianPlayerLinks.AnyAsync(x => x.AcademyId == academy && x.GuardianId == guardian && x.PlayerId == player, ct)) { db.Add(new GuardianPlayerLink { Id = Guid.NewGuid(), AcademyId = academy, GuardianId = guardian, PlayerId = player, CreatedByUserId = actor, RelationshipType = relation, CreatedAtUtc = now, UpdatedAtUtc = now }); await db.SaveChangesAsync(ct); } }
    private static async Task Enroll(FoundationDbContext db, Guid academy, Guid player, Guid sport, Guid branch, Guid group, DateTimeOffset now, CancellationToken ct) { if (!await db.SportEnrollments.AnyAsync(x => x.AcademyId == academy && x.PlayerId == player && x.SportId == sport, ct)) { db.Add(new SportEnrollment { Id = Guid.NewGuid(), AcademyId = academy, PlayerId = player, SportId = sport, BranchId = branch, TrainingGroupId = group, Status = EnrollmentStatus.Active, CreatedAtUtc = now, UpdatedAtUtc = now }); await db.SaveChangesAsync(ct); } }
    private static Guid Deterministic(string value) { var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)); return new Guid(bytes[..16]); }
    private static async Task<ApplicationUser> EnsureGuardianUser(UserManager<ApplicationUser> users, FoundationDbContext db, Guid academy, string phone, string name, DateTimeOffset now, CancellationToken ct) { var user = await users.Users.SingleOrDefaultAsync(x => x.PhoneNumber == phone, ct); if (user is null) { user = new ApplicationUser { Id = Guid.NewGuid(), UserName = phone, PhoneNumber = phone, PhoneNumberConfirmed = true, DisplayName = name, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now }; var r = await users.CreateAsync(user); if (!r.Succeeded) throw new InvalidOperationException(string.Join(";", r.Errors.Select(x => x.Description))); } if (!await db.AcademyMemberships.AnyAsync(x => x.AcademyId == academy && x.UserId == user.Id, ct)) { db.Add(new AcademyMembership { Id = Guid.NewGuid(), AcademyId = academy, UserId = user.Id, Role = AcademyRole.Guardian, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now }); await db.SaveChangesAsync(ct); } return user; }
}
