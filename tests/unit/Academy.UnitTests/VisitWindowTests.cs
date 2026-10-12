using Academy.Infrastructure.Facilities;
namespace Academy.UnitTests;
public class VisitWindowTests
{
    [Theory]
    [InlineData("/api/v1/facilities/visits/123/exit","Edit")]
    [InlineData("/api/v1/facilities/bookings/123/entry","Add")]
    public void Admission_and_checkout_use_independent_CRUD_grants(string path,string action) => Assert.Equal(action,Academy.Api.Auth.StaffAccessRules.Operation("POST",path));
    [Theory]
    [InlineData(-16,false)] [InlineData(-15,true)] [InlineData(0,true)] [InlineData(59,true)] [InlineData(60,false)]
    public void Entry_window_is_explicit_and_half_open(int minutes,bool admitted)
    {
        var (from,to)=VisitWindow.For(new(2026,10,12),new(10,0),new(11,0),TimeZoneInfo.Utc);
        Assert.Equal(admitted,VisitWindow.Contains(new DateTimeOffset(2026,10,12,10,0,0,TimeSpan.Zero).AddMinutes(minutes),from,to));
    }
    [Fact]
    public void Ambiguous_or_invalid_local_times_require_review_instead_of_guessing()
    {
        var zone=TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        Assert.Throws<ArgumentException>(()=>VisitWindow.For(new(2026,11,1),new(1,30),new(2,30),zone));
        Assert.Throws<ArgumentException>(()=>VisitWindow.For(new(2026,3,8),new(2,30),new(3,30),zone));
    }
}
