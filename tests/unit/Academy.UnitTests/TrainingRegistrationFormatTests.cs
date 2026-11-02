using Academy.Api.Slice3;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Subscriptions;
namespace Academy.UnitTests;
public sealed class TrainingRegistrationFormatTests
{
    [Theory]
    [InlineData(TrainingRegistrationFormat.Group, TrainingFormat.Group, true)]
    [InlineData(TrainingRegistrationFormat.Group, TrainingFormat.Season, true)]
    [InlineData(TrainingRegistrationFormat.Group, TrainingFormat.Private, false)]
    [InlineData(TrainingRegistrationFormat.Group, TrainingFormat.SemiPrivate, false)]
    [InlineData(TrainingRegistrationFormat.Private, TrainingFormat.Private, true)]
    [InlineData(TrainingRegistrationFormat.Private, TrainingFormat.Group, false)]
    [InlineData(TrainingRegistrationFormat.Private, TrainingFormat.Season, false)]
    [InlineData(TrainingRegistrationFormat.Private, TrainingFormat.SemiPrivate, false)]
    [InlineData(TrainingRegistrationFormat.SemiPrivate, TrainingFormat.SemiPrivate, true)]
    [InlineData(TrainingRegistrationFormat.SemiPrivate, TrainingFormat.Private, false)]
    [InlineData(TrainingRegistrationFormat.SemiPrivate, TrainingFormat.Season, false)]
    [InlineData(TrainingRegistrationFormat.SemiPrivate, TrainingFormat.Group, false)]
    [InlineData((TrainingRegistrationFormat)99, TrainingFormat.Group, false)]
    public void Product_format_must_match_the_registration_and_season_keeps_group_identity(TrainingRegistrationFormat registration, TrainingFormat plan, bool allowed) => Assert.Equal(allowed, PlanCatalog.FormatMatches(registration, plan));
}
