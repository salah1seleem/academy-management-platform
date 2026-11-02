using Academy.Infrastructure.Attendance;
namespace Academy.UnitTests;
public sealed class AttendanceStateTests
{
    [Theory]
    [InlineData(AttendanceStatus.Present, true)]
    [InlineData(AttendanceStatus.Late, true)]
    [InlineData(AttendanceStatus.Absent, false)]
    [InlineData(AttendanceStatus.Excused, false)]
    [InlineData(AttendanceStatus.NotRecorded, false)]
    public void Only_present_and_late_deliver_a_session(AttendanceStatus state, bool attended) => Assert.Equal(attended, AttendanceRules.Attended(state));
}
