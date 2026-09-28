namespace Academy.Api.Slice3;

public interface ISubscriptionClock { DateOnly Today { get; } DateTimeOffset UtcNow { get; } }

public sealed class SubscriptionClock(IConfiguration configuration, IHostEnvironment environment, TimeProvider clock) : ISubscriptionClock
{
    public DateTimeOffset UtcNow => clock.GetUtcNow();
    public DateOnly Today
    {
        get
        {
            var configured = configuration["Demo:ReferenceDate"];
            if (environment.IsEnvironment("Demo") && DateOnly.TryParse(configured, out var value)) return value;
            return DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        }
    }
}
