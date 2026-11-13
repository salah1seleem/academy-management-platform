using Academy.Api.Auth;

namespace Academy.Api.Slice3;

public interface ISubscriptionClock
{
    DateOnly Today { get; }
    DateTimeOffset UtcNow { get; }
    DateOnly DateAt(DateTimeOffset instant, string timeZone);
}

public sealed class SubscriptionClock(IConfiguration configuration, IHostEnvironment environment, TimeProvider clock, IHttpContextAccessor? request = null) : ISubscriptionClock
{
    // Demo business records share the reference date. Authentication/token expiry
    // continue to use TimeProvider directly and therefore real elapsed time.
    public DateTimeOffset UtcNow
    {
        get
        {
            var actual = clock.GetUtcNow();
            if (!environment.IsEnvironment("Demo") || !DateOnly.TryParse(configuration["Demo:ReferenceDate"], out var day)) return actual;
            // Anchor the reference business date in the Demo academy time zone,
            // not UTC: late UTC hours must not appear on the following Cairo day.
            var zone = TimeZoneInfo.FindSystemTimeZoneById(configuration["Demo:ReferenceTimeZone"] ?? "Africa/Cairo");
            var local = TimeZoneInfo.ConvertTime(actual, zone);
            var referenceLocal = day.ToDateTime(TimeOnly.FromDateTime(local.DateTime), DateTimeKind.Unspecified);
            return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(referenceLocal, zone));
        }
    }
    public DateOnly Today => DateAt(clock.GetUtcNow(),
        request?.HttpContext?.Features.Get<TenantMembership>()?.TimeZone ?? TimeZoneInfo.Utc.Id);

    public DateOnly DateAt(DateTimeOffset instant, string timeZone)
    {
        var configured = configuration["Demo:ReferenceDate"];
        if (environment.IsEnvironment("Demo") && DateOnly.TryParse(configured, out var value)) return value;
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZoneInfo.FindSystemTimeZoneById(timeZone)).DateTime);
    }
}
