using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Academy.Api.Auth;

public sealed class IdentityCookieOptions(DatabaseTicketStore ticketStore, IHostEnvironment environment)
    : IPostConfigureOptions<CookieAuthenticationOptions>
{
    public void PostConfigure(string? name, CookieAuthenticationOptions options)
    {
        if (name != IdentityConstants.ApplicationScheme) return;
        options.SessionStore = ticketStore;
        options.Cookie.Name = "academy.session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = environment.IsDevelopment() || environment.IsEnvironment("Demo")
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }
}
