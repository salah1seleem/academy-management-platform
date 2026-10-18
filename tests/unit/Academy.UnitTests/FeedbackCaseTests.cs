using Academy.Api.Operations;
namespace Academy.UnitTests;
public sealed class FeedbackCaseTests
{
    [Fact]
    public void Lifecycle_never_resolves_without_a_family_reply_or_closes_without_resolution()
    {
        var allowed = new[] { ("Open","InProgress"),("Reviewed","InProgress"),("Reopened","InProgress"),("AwaitingInfo","InProgress"),("InProgress","AwaitingInfo"),("Resolved","Closed"),("Resolved","Reopened"),("Closed","Reopened") };
        var states = new[] { "Open", "Reviewed", "InProgress", "AwaitingInfo", "Resolved", "Closed", "Reopened", "Unknown" };
        foreach (var from in states)
        foreach (var to in states)
            Assert.Equal(allowed.Contains((from,to)), GuardianFeedbackEndpoints.CanTransition(from,to));
    }
}
