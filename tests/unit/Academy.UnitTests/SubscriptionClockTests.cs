using Academy.Api.Auth;
using Academy.Api.Slice3;
using Academy.Infrastructure.Tenancy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;

namespace Academy.UnitTests;

public sealed class SubscriptionClockTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(20)]
    [InlineData(23)]
    public void Demo_writes_stay_on_reference_business_day_in_Cairo(int hour)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:ReferenceDate"] = "2026-09-28" }).Build();
        var clock = new SubscriptionClock(configuration, new Environment(), new FixedTime(new DateTimeOffset(2026, 10, 8, hour, 0, 0, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 9, 28), DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(clock.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")).DateTime));
        Assert.Equal(new DateOnly(2026, 9, 28), clock.Today);
        Assert.Equal(new DateOnly(2026, 9, 28), clock.DateAt(clock.UtcNow, "Pacific/Honolulu"));
    }

    [Theory]
    [InlineData("Africa/Cairo", "2026-11-11T21:59:59Z", "2026-11-11")]
    [InlineData("Africa/Cairo", "2026-11-11T22:00:00Z", "2026-11-12")]
    [InlineData("Africa/Cairo", "2026-07-11T20:59:59Z", "2026-07-11")]
    [InlineData("Africa/Cairo", "2026-07-11T21:00:00Z", "2026-07-12")]
    [InlineData("Pacific/Honolulu", "2026-11-11T22:00:00Z", "2026-11-11")]
    public void Resolves_business_day_in_the_bound_academy_zone_and_preserves_UTC(string zone, string instant, string expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Demo:ReferenceDate"] = "2026-09-28" }).Build();
        var actual = DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture);
        var context = new DefaultHttpContext();
        context.Features.Set(new TenantMembership(Guid.NewGuid(), "أكاديمية اختبار", AcademyRole.AcademyOwner) { TimeZone = zone });
        var clock = new SubscriptionClock(configuration, new Environment { EnvironmentName = "Production" }, new FixedTime(actual), new HttpContextAccessor { HttpContext = context });

        Assert.Equal(DateOnly.ParseExact(expected, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), clock.Today);
        Assert.Equal(clock.Today, clock.DateAt(actual, zone));
        Assert.Equal(actual, clock.UtcNow);
    }
    private sealed class FixedTime(DateTimeOffset now) : TimeProvider { public override DateTimeOffset GetUtcNow() => now; }
    private sealed class Environment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Demo";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
