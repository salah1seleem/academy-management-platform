using System.Security.Claims;
using System.Security.Cryptography;
using Academy.Api.Auth;
using Academy.Api.Facilities;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using QRCoder;

namespace Academy.Api.Slice4;
public sealed record PlayerCardWrite(uint ExpectedVersion);
public sealed record PlayerCardResolve(string Code);
public sealed record PlayerCardView(bool Active, uint Version, string Message, string? Code = null, string? PngBase64 = null, DateTimeOffset? ExpiresAtUtc = null, string? PlayerName = null, string? PlayerCode = null);
public sealed class PlayerCardService(FoundationDbContext db, CurrentTenant tenant, IHttpContextAccessor http, IAuthorizationService auth, IDataProtectionProvider protection, TimeProvider clock)
{
    private ClaimsPrincipal Principal => http.HttpContext!.User;
    private Guid Actor => Guid.Parse(Principal.FindFirstValue(ClaimTypes.NameIdentifier)!);
    private IDataProtector Protector(Guid academy) => protection.CreateProtector("Academity.PlayerCard.v1", academy.ToString("N"));
    private async Task<TenantMembership> Authority(string action, bool issuing = false)
    {
        var t = await tenant.ResolveAsync() ?? throw new FacilityRejected("يلزم تسجيل الدخول مجددًا.", 401);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"grants:{t.AcademyId}:{t.MembershipId}"},0))");
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"config:" + t.AcademyId},0))");
        t = await tenant.ResolveAsync() ?? throw new FacilityRejected("العضوية غير متاحة.", 403);
        if (issuing && t.Role is not (AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin) || !(await auth.AuthorizeAsync(Principal, AcademyPermissions.AttendanceManage)).Succeeded) throw new FacilityRejected("صلاحية استخدام بطاقة الحضور غير متاحة.", 403);
        var member = await db.AcademyMemberships.AsNoTracking().SingleAsync(m => m.Id == t.MembershipId);
        if (t.Role == AcademyRole.AcademyAdmin && member.UseActionGrants) {
            var grant = await db.StaffActionGrants.AsNoTracking().SingleOrDefaultAsync(g => g.MembershipId == member.Id && g.Module == "attendance" && g.IsActive);
            if (grant?.Read != true || action == "Add" && !grant.Add || action == "Edit" && !grant.Edit) throw new FacilityRejected("الإجراء خارج صلاحيات الحضور الممنوحة.", 403);
        }
        return t;
    }
    private async Task Player(Guid academy, Guid player)
    {
        if (!await db.Players.AnyAsync(p => p.AcademyId == academy && p.Id == player && p.IsActive && db.SportEnrollments.Any(e => e.PlayerId == p.Id && e.IsActive && e.Branch.IsActive && e.Sport.IsActive))) throw new FacilityRejected("اللاعب غير متاح ضمن نطاقك.", 404);
    }
    public async Task<object> Read(Guid player, bool guardian)
    {
        var t = await tenant.ResolveAsync() ?? throw new FacilityRejected("يلزم تسجيل الدخول.", 401); await Player(t.AcademyId, player);
        if (guardian) {
            if (t.Role != AcademyRole.Guardian || !await db.GuardianPlayerLinks.AnyAsync(l => l.AcademyId == t.AcademyId && l.PlayerId == player && l.IsActive && l.Guardian.IsActive && l.Guardian.UserId == Actor)) throw new FacilityRejected("البطاقة غير متاحة.", 404);
        } else if (t.Role is not (AcademyRole.AcademyOwner or AcademyRole.AcademyAdmin)) throw new FacilityRejected("البطاقة غير متاحة.", 403);
        var card = await db.PlayerCards.AsNoTracking().SingleOrDefaultAsync(c => c.AcademyId == t.AcademyId && c.PlayerId == player && c.IsActive);
        var identity = await db.Players.Where(p => p.Id == player && p.AcademyId == t.AcademyId).Select(p => new { p.ArabicName, p.PlayerCode }).SingleAsync();
        return (card is null ? new PlayerCardView(false, 0, "لم تصدر بطاقة فعالة. يمكن استخدام الاسم أو كود اللاعب يدويًا.") : Render(card)) with { PlayerName = identity.ArabicName, PlayerCode = identity.PlayerCode };
    }
    private PlayerCardView Render(PlayerCard card)
    {
        var active = card.IsActive && card.ExpiresAtUtc > clock.GetUtcNow();
        if (!active) return new(false, card.Version, "انتهت صلاحية البطاقة. اطلب إصدار بطاقة جديدة من الإدارة.", ExpiresAtUtc: card.ExpiresAtUtc);
        string code;
        try { code = Protector(card.AcademyId).Unprotect(card.ProtectedToken); }
        catch (CryptographicException) { throw new FacilityRejected("تعذر قراءة البطاقة القديمة. ألغها وأصدر بطاقة جديدة.", 409); }
        using var data = QRCodeGenerator.GenerateQrCode(code, QRCodeGenerator.ECCLevel.Q); using var png = new PngByteQRCode(data);
        return new(true, card.Version, "اعرض البطاقة لموظف الحضور؛ لا تثبت الهوية أو الدفع بذاتها.", code, Convert.ToBase64String(png.GetGraphic(8)), card.ExpiresAtUtc);
    }
    public async Task<object> Issue(Guid player, PlayerCardWrite body, string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 100) throw new FacilityRejected("معرف العملية مطلوب وضمن الحد المسموح.", 422);
        await using var tx = await db.Database.BeginTransactionAsync(); var t = await Authority("Add", true); await Player(t.AcademyId, player);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"player-card:{t.AcademyId}:{player}"},0))");
        var retry = await db.PlayerCards.SingleOrDefaultAsync(c => c.AcademyId == t.AcademyId && c.IssuedByUserId == Actor && c.RequestKey == key);
        if (retry is not null) {
            if (retry.PlayerId != player || retry.PreviousVersion != body.ExpectedVersion || !retry.IsActive || retry.ExpiresAtUtc <= clock.GetUtcNow()) throw new FacilityRejected("معرف العملية مستخدم لبطاقة أخرى أو منتهية؛ راجع البطاقة الحالية.", 409);
            await tx.CommitAsync(); return Render(retry);
        }
        var previous = await db.PlayerCards.SingleOrDefaultAsync(c => c.AcademyId == t.AcademyId && c.PlayerId == player && c.IsActive);
        if ((previous?.Version ?? 0) != body.ExpectedVersion) throw new FacilityRejected("تغيرت البطاقة؛ أعد تحميلها قبل إصدار بديل.", 409);
        var now = clock.GetUtcNow();
        if (previous is not null) { previous.IsActive = false; previous.RevokedAtUtc = now; previous.RevokedByUserId = Actor; previous.UpdatedAtUtc = now; await db.SaveChangesAsync(); }
        var token = PlayerCardCode.Create();
        var card = new PlayerCard { Id = Guid.NewGuid(), AcademyId = t.AcademyId, PlayerId = player, TokenHash = PlayerCardCode.Hash(token), ProtectedToken = Protector(t.AcademyId).Protect(token), ExpiresAtUtc = now.AddDays(365), IssuedByUserId = Actor, RequestKey = key, PreviousVersion = body.ExpectedVersion, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.PlayerCards.Add(card); await db.SaveChangesAsync(); await tx.CommitAsync(); return Render(card);
    }
    public async Task<object> Revoke(Guid player, PlayerCardWrite body)
    {
        await using var tx = await db.Database.BeginTransactionAsync(); var t = await Authority("Edit", true); await Player(t.AcademyId, player);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"player-card:{t.AcademyId}:{player}"},0))");
        var card = await db.PlayerCards.SingleOrDefaultAsync(c => c.AcademyId == t.AcademyId && c.PlayerId == player && c.IsActive);
        if (card is null) return new { revoked = true };
        if (card.Version != body.ExpectedVersion) throw new FacilityRejected("تغيرت البطاقة؛ أعد تحميلها قبل الإلغاء.", 409);
        card.IsActive = false; card.RevokedAtUtc = card.UpdatedAtUtc = clock.GetUtcNow(); card.RevokedByUserId = Actor;
        await db.SaveChangesAsync(); await tx.CommitAsync(); return new { revoked = true };
    }
    public async Task<object> Resolve(Guid sessionId, PlayerCardResolve body)
    {
        // Strictly a lookup: no attendance, subscription, access or financial side-effect.
        if (!PlayerCardCode.IsWellFormed(body.Code)) throw new FacilityRejected("البطاقة غير متاحة لهذه الحصة.", 404);
        await using var tx = await db.Database.BeginTransactionAsync(); var t = await Authority("Read");
        var session = await db.TrainingSessions.AsNoTracking().SingleOrDefaultAsync(s => s.AcademyId == t.AcademyId && s.Id == sessionId && s.TrainingGroup.IsActive && s.Branch.IsActive && s.Sport.IsActive) ?? throw new FacilityRejected("الحصة غير متاحة.", 404);
        if (t.Role == AcademyRole.Coach && !await db.StaffGroupAssignments.AnyAsync(a => a.AcademyId == t.AcademyId && a.TrainingGroupId == session.TrainingGroupId && a.AcademyMembershipId == t.MembershipId && a.IsActive)) throw new FacilityRejected("الحصة غير متاحة.", 404);
        if (session.SessionDate > await AttendanceCalendar.TodayAsync(t.AcademyId, db, clock) || session.Status == TrainingSessionStatus.Cancelled) throw new FacilityRejected("لا يمكن استخدام بطاقة لحصة مستقبلية أو ملغاة.", 409);
        var hash = PlayerCardCode.Hash(body.Code); var card = await db.PlayerCards.AsNoTracking().SingleOrDefaultAsync(c => c.AcademyId == t.AcademyId && c.TokenHash == hash && c.IsActive && c.ExpiresAtUtc > clock.GetUtcNow()) ?? throw new FacilityRejected("البطاقة غير متاحة لهذه الحصة.", 404);
        var enrollment = await db.SportEnrollments.AsNoTracking().Where(e => e.AcademyId == t.AcademyId && e.PlayerId == card.PlayerId && e.Player.IsActive && e.IsActive && e.Status == EnrollmentStatus.Active && FoundationDbContext.IsSessionAssigned(e.AcademyId, e.Id, sessionId)).Select(e => new { sportEnrollmentId = e.Id, playerId = e.PlayerId, e.Player.ArabicName, e.Player.PlayerCode }).SingleOrDefaultAsync() ?? throw new FacilityRejected("البطاقة غير متاحة لهذه الحصة.", 404);
        await tx.CommitAsync(); return new { player = enrollment, identityCheckRequired = true, message = "تحقق بصريًا من اللاعب ثم اختر الحالة واحفظ؛ قراءة البطاقة وحدها لا تسجل حضورًا." };
    }
}
