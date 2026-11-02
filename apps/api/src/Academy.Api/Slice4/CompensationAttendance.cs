using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.People;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice4;
public sealed partial class AttendanceService
{
    private async Task<bool> ConsumeCompensation(PlayerAttendance attendance, DateOnly date, Guid actor, CancellationToken ct)
    {
        var booking = await db.CompensationBookings.SingleOrDefaultAsync(b => b.AcademyId == attendance.AcademyId && b.SportEnrollmentId == attendance.SportEnrollmentId && b.TrainingSessionId == attendance.TrainingSessionId && b.State == CompensationBookingState.Reserved, ct);
        if (booking is null) return false;
        var grant = await db.CompensationGrants.SingleAsync(g => g.AcademyId == attendance.AcademyId && g.Id == booking.GrantId, ct);
        if (date < grant.ValidFrom || date > grant.ValidTo) throw new AttendanceConflictException("الحصة خارج صلاحية التعويض.");
        var used = await db.CompensationMovements.Where(m => m.AcademyId == attendance.AcademyId && m.GrantId == grant.Id).SumAsync(m => (int?)m.Units, ct) ?? 0;
        if (used + booking.Units > grant.Units) throw new AttendanceConflictException("رصيد التعويض لا يكفي.");
        db.CompensationMovements.Add(new() { Id = Guid.NewGuid(), AcademyId = attendance.AcademyId, BranchId = booking.BranchId, GrantId = grant.Id, BookingId = booking.Id, PlayerAttendanceId = attendance.Id, Units = booking.Units, ActorUserId = actor, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() });
        return true;
    }
    private async Task RestoreCompensation(PlayerAttendance attendance, Guid actor, CancellationToken ct)
    {
        var movement = await db.CompensationMovements.Where(m => m.AcademyId == attendance.AcademyId && m.PlayerAttendanceId == attendance.Id && m.ReversesMovementId == null && !db.CompensationMovements.Any(r => r.AcademyId == attendance.AcademyId && r.ReversesMovementId == m.Id)).SingleOrDefaultAsync(ct);
        if (movement is null) return;
        db.CompensationMovements.Add(new() { Id = Guid.NewGuid(), AcademyId = attendance.AcademyId, BranchId = movement.BranchId, GrantId = movement.GrantId, BookingId = movement.BookingId, PlayerAttendanceId = attendance.Id, Units = -movement.Units, ReversesMovementId = movement.Id, ActorUserId = actor, CreatedAtUtc = clock.GetUtcNow(), UpdatedAtUtc = clock.GetUtcNow() });
    }
}
