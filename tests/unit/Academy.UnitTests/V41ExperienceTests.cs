using Academy.Api.Operations;
using Academy.Api.Help;
using Academy.Api.Auth;
namespace Academy.UnitTests;
public sealed class V41ExperienceTests
{
    [Fact] public void Three_stars_with_85_outrank_two_with_87_and_never_replace_evaluation()
    {
        Assert.Equal(92.5m,MonthlyStarRanking.Index(85,3));
        Assert.True(MonthlyStarRanking.Index(85,3)>MonthlyStarRanking.Index(87,2));
        Assert.Equal(MonthlyStarRanking.Index(85,3),MonthlyStarRanking.Index(85,6));
        Assert.Equal(0,MonthlyStarRanking.Index(0,0));
        Assert.Throws<ArgumentOutOfRangeException>(()=>MonthlyStarRanking.Index(101,1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>MonthlyStarRanking.Index(85,-1));
    }
    [Theory]
    [InlineData("https://cdn.example.test/شرح.mp4",true)]
    [InlineData("http://cdn.example.test/video.mp4",false)]
    [InlineData("https://127.0.0.1/video.mp4",false)]
    [InlineData("https://localhost/video.mp4",false)]
    [InlineData("https://private.local/video.mp4",false)]
    [InlineData("https://secret@cdn.example.test/video.mp4",false)]
    public void Help_urls_are_safe_configuration_not_fabricated_content(string url,bool valid)=>Assert.Equal(valid,PageHelpEndpoints.SafeUrl(url));
    [Theory]
    [InlineData("/api/v1/notifications","GET",true)]
    [InlineData("/api/v1/notifications/318adfa6-ce9e-4d2b-a1f2-48b7bdf0675c/read","PUT",true)]
    [InlineData("/api/v1/notifications/other/read","PUT",false)]
    [InlineData("/api/v1/manage/notifications/send","POST",false)]
    [InlineData("/api/v1/page-help/home","GET",true)]
    [InlineData("/api/v1/page-help/arbitrary","GET",false)]
    [InlineData("/api/v1/manage/page-help","PUT",false)]
    public void Own_experience_exception_does_not_open_management(string path,string method,bool allowed)=>Assert.Equal(allowed,MembershipScope.IsOwnExperiencePath(path,method));
}
