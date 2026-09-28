using Microsoft.AspNetCore.Antiforgery;

namespace Academy.Api.Auth;

public sealed class CsrfFilter(IAntiforgery antiforgery) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
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
