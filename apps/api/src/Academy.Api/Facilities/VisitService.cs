using System.Security.Cryptography;
using System.Text.Json;
using Academy.Api.Configuration;
using Academy.Api.Slice3;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Facilities;
public sealed record EntryWrite(Guid? EnrollmentId, Guid? GuardianId = null, Guid? AccompanyingVisitId = null, bool IdentityConfirmed = false, Guid? DayPassTicketId = null);
public sealed record ExitWrite(uint ExpectedVersion);
public sealed record VisitCardWrite(string Code);
public sealed partial class VisitService(FoundationDbContext db, FacilityService facilities, TimeProvider clock)
{
    private static string Hash(object body) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(body)));
    private static void Key(string key) { if (string.IsNullOrWhiteSpace(key) || key.Length > 100) throw new FacilityRejected("معرف عملية صالح مطلوب.", 422); }
    private async Task Intent(Guid academy, Guid actor, string key) => await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"entry-intent:{academy}:{actor}:{key}"},0))");
    private async Task<ResourceBooking> Booking(Guid id) => await db.ResourceBookings.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id) ?? throw new FacilityRejected("الحجز غير متاح في نطاقك.", 404);
    private async Task<(DateTimeOffset From, DateTimeOffset To)> Window(ResourceBooking booking)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById(await db.Academies.Where(a => a.Id == booking.AcademyId).Select(a => a.TimeZone).SingleAsync());
        if (booking.Date != DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).DateTime)) throw new FacilityRejected("الدخول لحجز اليوم الفعلي فقط؛ لا تسجل زيارة مستقبلية أو بأثر رجعي.");
        try { var window = VisitWindow.For(booking.Date, booking.StartTime, booking.EndTime, zone); if (!VisitWindow.Contains(clock.GetUtcNow(), window.From, window.To)) throw new FacilityRejected("الدخول متاح من15 دقيقة قبل الحجز وحتى نهايته فقط."); return window; }
        catch (ArgumentException) { throw new FacilityRejected("توقيت الحجز يحتاج مراجعة فرق التوقيت قبل الدخول."); }
    }
    private async Task Available(ResourceBooking booking, FacilityResource resource)
    {
        if (booking.State != ResourceBookingState.Reserved || !resource.IsActive || resource.ApprovedAtUtc is null || !resource.ServiceKinds.Contains(booking.ServiceKind)
            || !await db.FacilitySpaces.AnyAsync(s => s.Id == resource.FacilitySpaceId && s.IsActive && db.Sports.Any(x => x.Id == s.SportId && x.IsActive) && db.Facilities.Any(f => f.Id == s.FacilityId && f.IsActive))
            || !await db.Branches.AnyAsync(b => b.Id == booking.BranchId && b.IsActive)
            || await db.ResourceClosures.AnyAsync(c => c.ResourceId == resource.Id && c.IsActive && c.FromDate <= booking.Date && c.ToDate >= booking.Date && c.StartTime < booking.EndTime && c.EndTime > booking.StartTime)) throw new FacilityRejected("الحجز أو المورد غير متاح للدخول.");
    }
    private async Task<AccessEntitlement> Participant(ResourceBooking booking, Guid enrollment, Guid actor, (DateTimeOffset From, DateTimeOffset To) window)
    {
        if (booking.ServiceKind != "Training" || booking.TrainingSessionId is not Guid sessionId) throw new FacilityRejected("الحجز وحده ليس تصريح دخول. تصاريح الزيارة والسباحة الحرة تتطلب مسار الخدمة المدفوعة.");
        var config = await db.AcademyConfigurationVersions.AsNoTracking().Where(c => c.State == ConfigurationState.Active).Select(c => c.Payload).SingleOrDefaultAsync();
        if (config is null || !AcademyRuntimeSettings.Read(config).Features.GetValueOrDefault("training") || !await db.AcademyFeatureEntitlements.AnyAsync(e => e.Feature == "training" && e.IsActive && e.Enabled)) throw new FacilityRejected("خدمة التدريب غير متاحة حاليًا.", 403);
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"attendance:{booking.AcademyId:N}:{sessionId:N}:{Guid.Empty:N}"},0))");
        var e = await db.SportEnrollments.FromSqlInterpolated($"SELECT *, xmin FROM \"SportEnrollments\" WHERE \"Id\"={enrollment} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync();
        if (e is not null) await db.Database.ExecuteSqlInterpolatedAsync($"SELECT 1 FROM \"Players\" WHERE \"AcademyId\"={booking.AcademyId} AND \"Id\"={e.PlayerId} FOR UPDATE");
        if (e is null || !e.IsActive || e.Status != EnrollmentStatus.Active || !await db.Players.AnyAsync(p => p.Id == e.PlayerId && p.IsActive)
            || !await db.TrainingSessions.AnyAsync(s => s.Id == sessionId && s.TrainingGroup.IsActive && s.SessionDate == booking.Date && s.StartTime == booking.StartTime && s.EndTime == booking.EndTime && s.Status != TrainingSessionStatus.Cancelled && s.BranchId == booking.BranchId && s.SportId == e.SportId && FoundationDbContext.IsSessionAssigned(e.AcademyId, e.Id, s.Id))) throw new FacilityRejected("اللاعب ليس عضوًا فعالًا في هذه الحصة.", 404);
        var entitlement = new AccessEntitlement { Id = Guid.NewGuid(), AcademyId = booking.AcademyId, BranchId = booking.BranchId, ResourceId = booking.ResourceId, ResourceBookingId = booking.Id, PersonKind = EntryPersonKind.Participant, PlayerId = e.PlayerId, SportEnrollmentId = e.Id, BookingDate = booking.Date, ValidFromUtc = window.From, ValidToUtc = window.To, AuthorizedByUserId = actor, Basis = "TrainingSubscription", CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
        var compensation = await db.CompensationBookings.AsNoTracking().SingleOrDefaultAsync(b => b.SportEnrollmentId == enrollment && b.TrainingSessionId == sessionId && b.State == CompensationBookingState.Reserved);
        if (compensation is not null) {
            var grant = await db.CompensationGrants.SingleAsync(g => g.Id == compensation.GrantId);
            var used = await db.CompensationMovements.Where(m => m.GrantId == grant.Id).SumAsync(m => (int?)m.Units) ?? 0;
            var usedHere = await db.CompensationMovements.Where(m => m.BookingId == compensation.Id).SumAsync(m => (int?)m.Units) ?? 0;
            if (booking.Date < grant.ValidFrom || booking.Date > grant.ValidTo || used - usedHere + compensation.Units > grant.Units) throw new FacilityRejected("تصريح التعويض غير صالح لهذه الحصة.");
            entitlement.CompensationBookingId = compensation.Id; entitlement.Basis = "ApprovedCompensation"; return entitlement;
        }
        // An already-recorded attendance may have consumed the last unit for THIS session.
        var attendance = await db.PlayerAttendances.AsNoTracking().SingleOrDefaultAsync(a => a.TrainingSessionId == sessionId && a.SportEnrollmentId == enrollment);
        var periods = await db.SubscriptionPeriods.FromSqlInterpolated($"SELECT *, xmin FROM \"SubscriptionPeriods\" WHERE \"SportEnrollmentId\"={enrollment} ORDER BY \"StartDate\",\"Id\" FOR UPDATE").Include(p => p.SubscriptionPlan).AsNoTracking().ToArrayAsync();
        var period = periods.FirstOrDefault(p => p.IsActive && p.Status is not (SubscriptionPeriodStatus.Cancelled or SubscriptionPeriodStatus.Frozen)
            && p.StartDate <= booking.Date && (!p.EndDate.HasValue || p.EndDate >= booking.Date)
            && (p.ServiceKindSnapshot ?? p.SubscriptionPlan.ServiceKind) == AcademyServiceKind.Training
            && PlanCatalog.FormatMatches(e.RegistrationFormat, p.TrainingFormatSnapshot ?? p.SubscriptionPlan.TrainingFormat)
            && ((p.PlanTypeSnapshot ?? p.SubscriptionPlan.PlanType) == SubscriptionPlanType.Duration || p.RemainingSessions > 0 || attendance is not null && AttendanceRules.Attended(attendance.Status) && attendance.ConsumedSubscriptionPeriodId == p.Id));
        if (period is null) throw new FacilityRejected("لا توجد خدمة تدريب صالحة: راجع الاشتراك أو التجميد أو رصيد الحصص. لم يسجل دخول أو حضور.");
        entitlement.SubscriptionPeriodId = period.Id; return entitlement;
    }
    private async Task<AccessEntitlement> DayPass(ResourceBooking booking, Guid ticketId, Guid actor, (DateTimeOffset From, DateTimeOffset To) window)
    {
        if (booking.ServiceKind != nameof(AcademyServiceKind.DayPass) || booking.TrainingSessionId.HasValue) throw new FacilityRejected("التذكرة لا تخص هذا الموعد.", 404);
        var ticket = await db.DayPassTickets.AsNoTracking().SingleOrDefaultAsync(x => x.Id == ticketId && x.ResourceBookingId == booking.Id && x.Status == DayPassTicketStatus.Confirmed && x.BranchId == booking.BranchId) ?? throw new FacilityRejected("لا توجد تذكرة زيارة مؤكدة لهذا الموعد.", 404);
        if (await db.AccessEntitlements.AnyAsync(x => x.DayPassTicketId == ticketId)) throw new FacilityRejected("استخدمت هذه التذكرة للدخول من قبل؛ لا تسجل زيارة ثانية.");
        if (!await db.Players.AnyAsync(x => x.Id == ticket.PlayerId && x.IsActive)) throw new FacilityRejected("ملف الطفل غير متاح.", 404);
        return new AccessEntitlement { Id = Guid.NewGuid(), AcademyId = booking.AcademyId, BranchId = booking.BranchId, ResourceId = booking.ResourceId, ResourceBookingId = booking.Id, PersonKind = EntryPersonKind.Participant, PlayerId = ticket.PlayerId, DayPassTicketId = ticketId, BookingDate = booking.Date, ValidFromUtc = window.From, ValidToUtc = window.To, AuthorizedByUserId = actor, Basis = "PaidDayPass", CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
    }
    public async Task<object> Enter(Guid bookingId, EntryWrite body, string key)
    {
        Key(key); var participant = body.EnrollmentId.HasValue || body.DayPassTicketId.HasValue;
        if (!body.IdentityConfirmed) throw new FacilityRejected("تحقق من هوية الشخص قبل تسجيل دخوله؛ البطاقة وحدها لا تكفي.", 422);
        if ((body.EnrollmentId.HasValue && body.DayPassTicketId.HasValue) || (participant ? body.GuardianId.HasValue || body.AccompanyingVisitId.HasValue : !body.GuardianId.HasValue || !body.AccompanyingVisitId.HasValue)) throw new FacilityRejected("اختر تسجيل تدريب أو تذكرة واحدة أو ولي أمر مرافقًا لزيارة طفل محددة.", 422);
        await using var tx = await db.Database.BeginTransactionAsync(); var (t, actor) = await facilities.Authority("Add"); var booking = await Booking(bookingId);
        await Intent(t.AcademyId, actor, key); var hash = Hash(new { bookingId, body });
        var retry = await db.EntryVisits.AsNoTracking().SingleOrDefaultAsync(v => v.EnteredByUserId == actor && v.EntryKey == key);
        if (retry is not null) { if (retry.EntryHash != hash) throw new FacilityRejected("معرف العملية مرتبط بدخول مختلف."); await tx.CommitAsync(); return await View(retry); }
        // Same lock order as timetable/membership moves: calendar -> resource -> attendance.
        if (booking.TrainingGroupId is Guid group) {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"timetable:{t.AcademyId}:{group}"},0))");
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"group-capacity:{t.AcademyId}:{group}"},0))");
        }
        var resource = await facilities.Lock(booking.ResourceId); booking = await Booking(bookingId); await Available(booking, resource); var window = await Window(booking);
        AccessEntitlement entitlement;
        if (participant) {
            entitlement = body.DayPassTicketId.HasValue ? await DayPass(booking, body.DayPassTicketId.Value, actor, window) : await Participant(booking, body.EnrollmentId!.Value, actor, window);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"visit-person:{t.AcademyId}:{entitlement.PlayerId}"},0))");
            if (await db.EntryVisits.IgnoreQueryFilters(["trusted-branches"]).AnyAsync(v => v.AcademyId == t.AcademyId && v.PlayerId == entitlement.PlayerId && v.ExitedAtUtc == null)) throw new FacilityRejected("للاعب زيارة مفتوحة؛ تحقق من الخروج قبل تسجيل زيارة أخرى.");
            if (await db.EntryVisits.CountAsync(v => v.ResourceBookingId == bookingId && v.PersonKind == EntryPersonKind.Participant && v.ExitedAtUtc == null) >= booking.ReservedSeats
                || await db.EntryVisits.CountAsync(v => v.ResourceId == resource.Id && v.PersonKind == EntryPersonKind.Participant && v.ExitedAtUtc == null) >= resource.Capacity) throw new FacilityRejected("عدد الزيارات المفتوحة بلغ سعة المشاركين المعتمدة؛ تحقق من الخروج ولا تتجاوز السعة.");
        } else {
            var child = await db.EntryVisits.SingleOrDefaultAsync(v => v.Id == body.AccompanyingVisitId && v.ResourceBookingId == bookingId && v.PersonKind == EntryPersonKind.Participant && v.ExitedAtUtc == null) ?? throw new FacilityRejected("اختر زيارة طفل مفتوحة من نفس الحجز.", 404);
            if (!await db.GuardianPlayerLinks.AnyAsync(l => l.PlayerId == child.PlayerId && l.GuardianId == body.GuardianId && l.IsActive && l.Guardian.IsActive)) throw new FacilityRejected("ولي الأمر غير مرتبط بهذا الطفل.", 404);
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"visit-person:{t.AcademyId}:{body.GuardianId}"},0))");
            if (await db.EntryVisits.IgnoreQueryFilters(["trusted-branches"]).AnyAsync(v => v.AcademyId == t.AcademyId && v.GuardianId == body.GuardianId && v.ExitedAtUtc == null)) throw new FacilityRejected("لولي الأمر زيارة مرافق مفتوحة بالفعل.");
            entitlement = new AccessEntitlement { Id = Guid.NewGuid(), AcademyId = t.AcademyId, BranchId = booking.BranchId, ResourceId = resource.Id, ResourceBookingId = booking.Id, PersonKind = EntryPersonKind.Companion, GuardianId = body.GuardianId, AccompanyingVisitId = child.Id, BookingDate = booking.Date, ValidFromUtc = window.From, ValidToUtc = window.To, AuthorizedByUserId = actor, Basis = "LinkedNonTrainingCompanion", CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() };
        }
        var now = clock.GetUtcNow(); if (!VisitWindow.Contains(now,window.From,window.To)) throw new FacilityRejected("انتهت نافذة الدخول أثناء التحقق؛ لم تسجل زيارة.");
        var row = new EntryVisit { Id = Guid.NewGuid(), AcademyId = t.AcademyId, BranchId = booking.BranchId, ResourceId = resource.Id, ResourceBookingId = booking.Id, AccessEntitlementId = entitlement.Id, PersonKind = entitlement.PersonKind, PlayerId = entitlement.PlayerId, GuardianId = entitlement.GuardianId, BookingDate = booking.Date, EnteredAtUtc = now, EnteredByUserId = actor, EntryKey = key, EntryHash = hash, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.AccessEntitlements.Add(entitlement); db.EntryVisits.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return await View(row);
    }
    public async Task<object> Exit(Guid id, ExitWrite body, string key)
    {
        Key(key); await using var tx = await db.Database.BeginTransactionAsync(); var (t, actor) = await facilities.Authority("Edit");
        var row = await db.EntryVisits.SingleOrDefaultAsync(v => v.Id == id) ?? throw new FacilityRejected("الزيارة غير متاحة.", 404);
        await Intent(t.AcademyId, actor, key); await facilities.Lock(row.ResourceId); await db.Entry(row).ReloadAsync(); var hash = Hash(new { id, body });
        if (row.ExitedAtUtc.HasValue) { if (row.ExitKey != key || row.ExitedByUserId != actor || row.ExitHash != hash) throw new FacilityRejected("سجل الخروج مثبت بالفعل؛ أعد تحميل الزيارة."); await tx.CommitAsync(); return await View(row); }
        if (row.Version != body.ExpectedVersion) throw new FacilityRejected("تغيرت الزيارة؛ أعد تحميل السجل قبل تسجيل الخروج.");
        if (row.PersonKind == EntryPersonKind.Participant && await db.EntryVisits.AnyAsync(v => v.ExitedAtUtc == null && db.AccessEntitlements.Any(a => a.Id == v.AccessEntitlementId && a.AccompanyingVisitId == id))) throw new FacilityRejected("سجل خروج المرافق المرتبط أولًا ثم خروج الطفل؛ لا نفترض خروج شخص تلقائيًا.");
        row.ExitedAtUtc = row.UpdatedAtUtc = clock.GetUtcNow(); row.ExitedByUserId = actor; row.ExitKey = key; row.ExitHash = hash;
        await db.SaveChangesAsync(); await tx.CommitAsync(); return await View(row);
    }
    private async Task<object> View(EntryVisit v)
    {
        var entitlement = await db.AccessEntitlements.AsNoTracking().SingleAsync(a => a.Id == v.AccessEntitlementId);
        var name = v.PlayerId.HasValue ? await db.Players.Where(p => p.Id == v.PlayerId).Select(p => p.ArabicName).SingleAsync() : await db.GuardianProfiles.Where(g => g.Id == v.GuardianId).Select(g => g.DisplayName).SingleAsync();
        return new { v.Id, v.Version, v.PersonKind, name, v.PlayerId, v.GuardianId, v.EnteredAtUtc, v.ExitedAtUtc, entitlement.Basis, entitlement.AccompanyingVisitId, staleOpen = !v.ExitedAtUtc.HasValue && clock.GetUtcNow() >= entitlement.ValidToUtc };
    }
    public async Task<object> Read(Guid bookingId)
    {
        var booking = await Booking(bookingId);
        var visits = await db.EntryVisits.AsNoTracking().Where(v => v.ResourceBookingId == bookingId).OrderByDescending(v => v.EnteredAtUtc).ToArrayAsync();
        var result = new List<object>(); foreach (var v in visits) result.Add(await View(v));
        var players = booking.TrainingSessionId is Guid session ? await db.SportEnrollments.AsNoTracking().Where(e => e.IsActive && e.Player.IsActive && e.Status == EnrollmentStatus.Active && FoundationDbContext.IsSessionAssigned(e.AcademyId, e.Id, session)).Select(e => new { enrollmentId = e.Id, e.PlayerId, name = e.Player.ArabicName, e.Player.PlayerCode }).ToArrayAsync() : [];
        var tickets = booking.ServiceKind == nameof(AcademyServiceKind.DayPass) ? await db.DayPassTickets.AsNoTracking().Where(x => x.ResourceBookingId == bookingId && x.Status == DayPassTicketStatus.Confirmed && x.Player.IsActive).Select(x => new { ticketId = x.Id, x.PlayerId, name = x.Player.ArabicName, x.Player.PlayerCode, used = db.AccessEntitlements.Any(a => a.DayPassTicketId == x.Id) }).ToArrayAsync() : [];
        var openChildren = visits.Where(v => v.PersonKind == EntryPersonKind.Participant && !v.ExitedAtUtc.HasValue).ToArray(); var children = openChildren.Select(v => v.PlayerId!.Value).ToArray();
        var guardians = await db.GuardianPlayerLinks.AsNoTracking().Where(l => children.Contains(l.PlayerId) && l.IsActive && l.Guardian.IsActive).Select(l => new { l.GuardianId, l.PlayerId, name = l.Guardian.DisplayName }).ToArrayAsync();
        var names = await db.Players.Where(p => children.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.ArabicName);
        var zone = TimeZoneInfo.FindSystemTimeZoneById(await db.Academies.Where(a => a.Id == booking.AcademyId).Select(a => a.TimeZone).SingleAsync()); var canEnter = false;
        try { var window = VisitWindow.For(booking.Date,booking.StartTime,booking.EndTime,zone); canEnter = booking.State == ResourceBookingState.Reserved && booking.Date == DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.GetUtcNow(),zone).DateTime) && VisitWindow.Contains(clock.GetUtcNow(),window.From,window.To); } catch (ArgumentException) { /* History remains readable when a local-time booking needs review. */ }
        return new { booking.Id, booking.ServiceKind, canEnter, visits = result, players, tickets, companions = guardians.SelectMany(g => openChildren.Where(v => v.PlayerId == g.PlayerId).Select(v => new { g.GuardianId, g.name, childName = names.GetValueOrDefault(v.PlayerId!.Value), accompanyingVisitId = v.Id })), message = "الزيارات المفتوحة تقدير إشغال، وليست عدًا فعليًا أو حضور حصة. المرافق غير متدرب." };
    }
    public async Task<object> Resolve(Guid bookingId, VisitCardWrite body)
    {
        if (!PlayerCardCode.IsWellFormed(body.Code)) throw new FacilityRejected("البطاقة غير متاحة لهذا الحجز.", 404);
        await using var tx = await db.Database.BeginTransactionAsync(); var (t, _) = await facilities.Authority("Add"); var booking = await Booking(bookingId);
        var hash = PlayerCardCode.Hash(body.Code); var player = await db.PlayerCards.AsNoTracking().Where(c => c.TokenHash == hash && c.IsActive && c.ExpiresAtUtc > clock.GetUtcNow()).Select(c => (Guid?)c.PlayerId).SingleOrDefaultAsync();
        var candidate = player.HasValue && booking.TrainingSessionId is Guid session ? await db.SportEnrollments.Where(e => e.PlayerId == player && e.IsActive && e.Player.IsActive && e.Status == EnrollmentStatus.Active && FoundationDbContext.IsSessionAssigned(t.AcademyId, e.Id, session)).Select(e => new { enrollmentId = e.Id, name = e.Player.ArabicName, e.Player.PlayerCode }).SingleOrDefaultAsync() : null;
        if (candidate is null) throw new FacilityRejected("البطاقة غير متاحة لهذا الحجز.", 404);
        await tx.CommitAsync(); return new { player = candidate, identityCheckRequired = true, message = "تحقق من هوية الطفل؛ ثم سجل الدخول صراحة. البطاقة وحدها ليست تصريحًا أو حضورًا." };
    }
    public async Task<object> GuardianHistory(Guid playerId)
    {
        if (!db.RequestScopeEnabled || !await db.GuardianPlayerLinks.AnyAsync(l => l.PlayerId == playerId && l.IsActive && l.Player.IsActive && l.Guardian.IsActive && l.Guardian.UserId == db.RequestUserId)) throw new FacilityRejected("سجل الطفل غير متاح.", 404);
        var config = await db.AcademyConfigurationVersions.Where(c => c.State == ConfigurationState.Active).Select(c => c.Payload).SingleOrDefaultAsync();
        if (config is null || !AcademyRuntimeSettings.Read(config).Features.GetValueOrDefault("facilities") || !await db.AcademyFeatureEntitlements.AnyAsync(e => e.Feature == "facilities" && e.IsActive && e.Enabled)) throw new FacilityRejected("خدمة سجل الزيارات غير متاحة حاليًا.", 403);
        var visits = await (from v in db.EntryVisits.AsNoTracking()
            join e in db.AccessEntitlements on v.AccessEntitlementId equals e.Id
            join r in db.FacilityResources on v.ResourceId equals r.Id
            join s in db.FacilitySpaces on r.FacilitySpaceId equals s.Id
            join f in db.Facilities on s.FacilityId equals f.Id
            where v.PlayerId == playerId && v.PersonKind == EntryPersonKind.Participant
            orderby v.EnteredAtUtc descending
            select new { v.Id, v.EnteredAtUtc, v.ExitedAtUtc, resource = r.Name, facility = f.Name, staleOpen = v.ExitedAtUtc == null && e.ValidToUtc <= clock.GetUtcNow() }).Take(100).ToArrayAsync();
        return new { visits, historyLimit = 100, message = "سجل زيارة طفلك مستقل عن حضور الحصة؛ الزيارة المفتوحة ليست عدًا فعليًا." };
    }
}
