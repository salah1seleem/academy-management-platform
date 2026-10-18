using Academy.Api.Auth;

namespace Academy.UnitTests;

public class V2AccessRulesTests
{
    [Theory]
    [InlineData("/api/v1/help/availability", "GET", true)]
    [InlineData("/api/v1/help/lessons", "GET", true)]
    [InlineData("/api/v1/help/lessons/player-attendance", "GET", true)]
    [InlineData("/api/v1/help/lessons/owner-dashboard", "POST", false)]
    [InlineData("/api/v1/help/lessons/owner-dashboard/publish", "GET", false)]
    [InlineData("/api/v1/help/unrecognized", "GET", false)]
    [InlineData("/api/v1/help/lessons/family-renewal/media/00000000-0000-0000-0000-000000000001/video", "GET", true)]
    [InlineData("/api/v1/help/lessons/evaluations/media/00000000-0000-0000-0000-000000000001/thumbnail", "GET", true)]
    [InlineData("/api/v1/help/lessons/evaluations/media/00000000-0000-0000-0000-000000000001/video", "POST", false)]
    [InlineData("/api/v1/help/lessons/evaluations/media/../video", "GET", false)]
    [InlineData("/api/v1/help/lessons/invented/media/00000000-0000-0000-0000-000000000001/video", "GET", false)]
    [InlineData("/api/v1/help/lessons/evaluations/media/00000000-0000-0000-0000-000000000001/edit", "GET", false)]
    public void Help_shared_paths_are_only_authenticated_reads_not_product_module_aliases(string path,string method,bool allowed)
    {
        Assert.Equal(allowed,Academy.Api.Help.HelpEndpoints.IsReadPath(path,method));
        Assert.Null(StaffAccessRules.ForPath(path));
        Assert.Equal(allowed,MembershipScope.AllowsNarrowSportPath(path,method));
    }
    [Theory]
    [InlineData("/api/v1/manage/staff/attendance", "attendance")]
    [InlineData("/api/v1/manage/staff/123/access", "staff")]
    [InlineData("/api/v1/reports/financial/export", "finance")]
    [InlineData("/api/v1/manage/structure/options", "academy")]
    [InlineData("/api/v1/manage/nutrition/items", "nutrition")]
    [InlineData("/api/v1/people/players", "players")]
    [InlineData("/api/v1/manage/family-renewals/123", "payments")]
    [InlineData("/api/v1/manage/family-renewals/123/review", "payments")]
    [InlineData("/api/v1/manage/family-renewals/quotes/123/quote", "payments")]
    [InlineData("/api/v1/my-followups", "followups")]
    [InlineData("/api/v1/my-followups/00000000-0000-0000-0000-000000000001", "followups")]
    public void Modules_have_deterministic_boundaries(string path, string module) => Assert.Equal(module, StaffAccessRules.ForPath(path));
    [Theory]
    [InlineData("GET", "/api/v1/people/players", "Read")]
    [InlineData("POST", "/api/v1/people/registration-quote", "Read")]
    [InlineData("POST", "/api/v1/manage/groups", "Add")]
    [InlineData("POST", "/api/v1/manage/structure/sessions/123/coach-override/preview", "Read")]
    [InlineData("PUT", "/api/v1/manage/structure/sessions/123/coach-override", "Edit")]
    [InlineData("PUT", "/api/v1/manage/groups/123/status", "Edit")]
    [InlineData("POST", "/api/v1/payments/123/approve", "Edit")]
    [InlineData("POST", "/api/v1/attendance/sessions/123/corrections", "Edit")]
    [InlineData("DELETE", "/api/v1/people/players/123", "Delete")]
    [InlineData("POST", "/api/v1/my-followups/00000000-0000-0000-0000-000000000001", "Edit")]
    public void Writes_do_not_inherit_read_permission(string method, string path, string operation) => Assert.Equal(operation, StaffAccessRules.Operation(method, path));
    [Fact] public void Unknown_modules_fail_closed() => Assert.Null(StaffAccessRules.ForPath("/api/v1/new-feature"));
    [Theory]
    [InlineData("/api/v1/people/players/00000000-0000-0000-0000-000000000001/refunds")]
    [InlineData("/api/v1/people/players/00000000-0000-0000-0000-000000000001/refunds/quote")]
    public void Refunds_require_finance_not_player_create(string path) => Assert.Equal("finance",StaffAccessRules.ForPath(path));
}
