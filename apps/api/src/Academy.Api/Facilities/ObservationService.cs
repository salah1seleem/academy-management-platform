using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Facilities;
public sealed record ObservationWrite(int Count);
public sealed class ObservationService(FoundationDbContext db, FacilityService facilities, TimeProvider clock)
{
    public async Task<object> Record(Guid bookingId, ObservationWrite body, string key)
    {
        if (body.Count < 0 || body.Count > 10000 || string.IsNullOrWhiteSpace(key) || key.Length > 100) throw new FacilityRejected("أدخل عددًا من0 إلى10000 ومعرف عملية صالحًا.", 422);
        await using var tx = await db.Database.BeginTransactionAsync(); var (t, actor) = await facilities.Authority("Add");
        var booking = await db.ResourceBookings.AsNoTracking().SingleOrDefaultAsync(b => b.AcademyId == t.AcademyId && b.Id == bookingId) ?? throw new FacilityRejected("الحجز غير متاح.", 404);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { bookingId, body.Count }))));
        await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"observation-intent:{t.AcademyId}:{actor}:{key}"},0))");
        var retry = await db.ResourceObservations.AsNoTracking().SingleOrDefaultAsync(o => o.AcademyId == t.AcademyId && o.ActorUserId == actor && o.RequestKey == key);
        if (retry is not null) { if (retry.RequestHash != hash) throw new FacilityRejected("معرف العملية مستخدم لعد مختلف."); await tx.CommitAsync(); return View(retry); }
        var resource = await facilities.Lock(booking.ResourceId); booking = await db.ResourceBookings.AsNoTracking().SingleAsync(b => b.Id == bookingId);
        var zone = await db.Academies.Where(a => a.Id == t.AcademyId).Select(a => a.TimeZone).SingleAsync(); var now = clock.GetUtcNow(); var local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(zone)); var today = DateOnly.FromDateTime(local.DateTime);
        if (booking.Date != today || booking.State != ResourceBookingState.Reserved || !resource.IsActive || resource.ApprovedAtUtc is null || !await db.FacilitySpaces.AnyAsync(s => s.Id == resource.FacilitySpaceId && s.IsActive && db.Facilities.Any(f => f.Id == s.FacilityId && f.IsActive))) throw new FacilityRejected("يسجل العد لحجز اليوم الفعلي على مورد فعال ومعتمد فقط.");
        int expected = booking.ReservedSeats; int? present = null, recorded = null;
        if (booking.TrainingSessionId is Guid session) {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtextextended({$"attendance:{t.AcademyId:N}:{session:N}:{Guid.Empty:N}"},0))");
            var counts = await db.TrainingSessions.Where(s => s.Id == session && s.AcademyId == t.AcademyId && s.SessionDate == today && s.Status != TrainingSessionStatus.Cancelled).Select(s => new {
                expected = db.SportEnrollments.Count(e => e.AcademyId == t.AcademyId && e.IsActive && e.Player.IsActive && e.Status == EnrollmentStatus.Active && FoundationDbContext.IsSessionAssigned(e.AcademyId,e.Id,s.Id)),
                present = db.PlayerAttendances.Count(a => a.AcademyId == t.AcademyId && a.TrainingSessionId == s.Id && (a.Status == AttendanceStatus.Present || a.Status == AttendanceStatus.Late)),
                recorded = db.PlayerAttendances.Count(a => a.AcademyId == t.AcademyId && a.TrainingSessionId == s.Id && a.Status != AttendanceStatus.NotRecorded)
            }).SingleOrDefaultAsync() ?? throw new FacilityRejected("الحصة ملغاة أو لا تطابق يوم الحجز."); expected = counts.expected; present = counts.present; recorded = counts.recorded;
        }
        var row = new ResourceObservation { Id = Guid.NewGuid(), AcademyId = t.AcademyId, BranchId = booking.BranchId, ResourceId = resource.Id, ResourceBookingId = booking.Id, TrainingSessionId = booking.TrainingSessionId, BookingDate = booking.Date, Sequence = (await db.ResourceObservations.Where(o => o.ResourceBookingId == booking.Id).MaxAsync(o => (int?)o.Sequence) ?? 0) + 1, Count = body.Count, ExpectedSnapshot = expected, RecordedPresentSnapshot = present, RecordedTotalSnapshot = recorded, ExpectedBasis = booking.TrainingSessionId.HasValue ? "Roster" : "ReservedSeats", Source = "Manual", ActorUserId = actor, RequestKey = key, RequestHash = hash, OutsideBookedHours = TimeOnly.FromDateTime(local.DateTime) < booking.StartTime || TimeOnly.FromDateTime(local.DateTime) >= booking.EndTime, CreatedAtUtc = now, UpdatedAtUtc = now };
        db.ResourceObservations.Add(row); await db.SaveChangesAsync(); await tx.CommitAsync(); return View(row);
    }
    public static object View(ResourceObservation o) => new { o.Id, o.Sequence, o.Count, o.ExpectedSnapshot, o.RecordedPresentSnapshot, o.RecordedTotalSnapshot, o.ExpectedBasis, o.Source, o.OutsideBookedHours, observedAtUtc = o.CreatedAtUtc, needsReview = ObservationVariance.NeedsReview(o.ExpectedSnapshot, o.RecordedPresentSnapshot, o.Count), deltaExpected = o.Count - o.ExpectedSnapshot, deltaPresent = o.RecordedPresentSnapshot.HasValue ? o.Count - o.RecordedPresentSnapshot : null, message = "فرق يحتاج تحققًا، وليس اتهامًا أو تسجيل حضور تلقائيًا." };
}
