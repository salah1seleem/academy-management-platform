using Academy.Api.Auth;

namespace Academy.Api.Configuration;

public sealed record AcademyFeatureMetadata(string Feature);

public sealed class AcademyFeatureGateMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, CurrentTenant tenant, AcademyConfigurationService config)
    {
        var path = context.Request.Path.Value ?? "";
        var feature = context.GetEndpoint()?.Metadata.GetMetadata<AcademyFeatureMetadata>()?.Feature ?? FeatureForPath(path);
        if (feature is not null)
        {
            var t = await tenant.ResolveAsync(context.RequestAborted);
            if (t is null || !await config.Enabled(t.AcademyId, feature))
            {
                await Results.Problem(statusCode: t is null ? 401 : 403, title: "الميزة غير متاحة لهذه الأكاديمية").ExecuteAsync(context);
                return;
            }
        }
        await next(context);
    }
    internal static string? FeatureForPath(string path)
    {
        if (path.StartsWith("/api/v1/content/nutrition", StringComparison.Ordinal) || path.StartsWith("/api/v1/guardian/nutrition", StringComparison.Ordinal) || path.Contains("/nutrition/", StringComparison.Ordinal)) return "nutrition";
        if (path.StartsWith("/api/v1/content/catalog", StringComparison.Ordinal) || path.StartsWith("/api/v1/guardian/catalog", StringComparison.Ordinal) || path.Contains("/product-sales", StringComparison.Ordinal)) return "catalog";
        if (path.StartsWith("/api/v1/medical-records", StringComparison.Ordinal) || path.StartsWith("/api/v1/guardian/children/", StringComparison.Ordinal) && path.EndsWith("/medical", StringComparison.Ordinal)) return "medical";
        if (path.StartsWith("/api/v1/player-media", StringComparison.Ordinal) || path.EndsWith("/media", StringComparison.Ordinal) || path == "/api/v1/assets/gallery") return "media";
        if (path.StartsWith("/api/v1/content/news", StringComparison.Ordinal) || path == "/api/v1/news") return "news";
        if (path.StartsWith("/api/v1/manage/matches", StringComparison.Ordinal) || path == "/api/v1/my-matches") return "matches";
        if (path.StartsWith("/api/v1/manage/payroll", StringComparison.Ordinal) || path.StartsWith("/api/v1/manage/employees", StringComparison.Ordinal) || path.StartsWith("/api/v1/staff/payroll", StringComparison.Ordinal)) return "payroll";
        return null;
    }
}
