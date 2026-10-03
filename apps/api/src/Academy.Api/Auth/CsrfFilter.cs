using Microsoft.AspNetCore.Antiforgery;
using Academy.Api.Mobile;

namespace Academy.Api.Auth;

public sealed class CsrfFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        // Native bearer credentials are not ambient cookies. A header alone never bypasses CSRF.
        if (context.HttpContext.User.Identity is { IsAuthenticated: true, AuthenticationType: MobileAuthenticationHandler.SchemeName })
            return await next(context);
        try
        {
            await antiforgery.ValidateRequestAsync(context.HttpContext);
        }
        catch (AntiforgeryValidationException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "طلب غير صالح",
                detail: "تعذر التحقق من حماية الطلب. حدّث الصفحة وحاول مرة أخرى.",
                type: "https://academy.example/problems/csrf");
        }
        return await next(context);
    }
}
