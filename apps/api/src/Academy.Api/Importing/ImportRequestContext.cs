using System.Security.Claims;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Importing;

// A platform operation carries the platform actor, never an invented owner membership.
public sealed record ImportScope(Guid AcademyId, Guid? MembershipId, AcademyRole? Role, bool Platform, string TimeZone);
public sealed class ImportRequestContext(IHttpContextAccessor accessor, CurrentTenant tenant, FoundationDbContext db)
{
    private ImportScope? resolved;
    public async Task<ImportScope> ResolveAsync()
    {
        if (resolved is not null) return resolved;
        var context = accessor.HttpContext!;
        if (context.Request.Path.StartsWithSegments("/api/v1/platform"))
        {
            if (!Guid.TryParse(context.Request.RouteValues["academyId"]?.ToString(), out var academy) ||
                !Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var actor)) throw new ImportRejected("authority");
            await ImportAuthority.CheckPlatform(db, academy, actor, requireTraining: !HttpMethods.IsGet(context.Request.Method));
            db.BindRequestScope(academy, actor, Guid.Empty, null);
            var zone = await db.Academies.Where(a => a.Id == academy).Select(a => a.TimeZone).SingleAsync(context.RequestAborted);
            return resolved = new(academy, null, null, true, zone);
        }
        var current = await tenant.ResolveAsync() ?? throw new ImportRejected("authority");
        return resolved = new(current.AcademyId, current.MembershipId, current.Role, false, current.TimeZone);
    }
    public Task CheckConnected(ImportScope scope) => scope.Platform ? Task.CompletedTask : ImportAuthority.CheckConnected(db, scope.AcademyId, scope.MembershipId!.Value);
    public async Task Check(ImportScope scope, Guid actor, bool balances)
    {
        if (scope.Platform) await ImportAuthority.CheckPlatform(db, scope.AcademyId, actor);
        else await ImportAuthority.Check(db, scope.AcademyId, actor, scope.MembershipId!.Value, balances);
    }
}
