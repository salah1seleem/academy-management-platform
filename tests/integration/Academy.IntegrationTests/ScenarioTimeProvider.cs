namespace Academy.IntegrationTests;

// Only injected by a test host. Production/Demo keep TimeProvider.System and
// the real academy day. Historical balance tests exercise their recorded day,
// not a privileged bypass of closed-day/future-attendance authorization.
internal sealed class ScenarioTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}
