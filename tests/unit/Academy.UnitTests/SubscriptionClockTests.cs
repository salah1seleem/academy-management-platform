using Academy.Api.Slice3;
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
