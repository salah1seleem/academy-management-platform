using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Facilities;
public static class VisitEndpoints
{
    public static void MapVisits(this WebApplication app)
    {
        app.MapGet("/api/v1/guardian/children/{id:guid}/visits", async (Guid id, VisitService service, HttpResponse response) => { response.Headers.CacheControl = "no-store, private"; return await Run(() => service.GuardianHistory(id)); }).RequireAuthorization(AcademyPermissions.GuardianChildrenRead);
        var api = app.MapGroup("/api/v1/facilities").RequireAuthorization(AcademyPermissions.FacilitiesRead);
        api.MapGet("/bookings/{id:guid}/visits", async (Guid id, VisitService service, HttpResponse response) => { response.Headers.CacheControl = "no-store, private"; return await Run(() => service.Read(id)); });
        api.MapPost("/bookings/{id:guid}/entry", async (Guid id, EntryWrite body, VisitService service, HttpRequest request) => await Run(() => service.Enter(id, body, request.Headers["Idempotency-Key"].ToString()))).RequireAuthorization(AcademyPermissions.FacilitiesManage).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/visits/{id:guid}/exit", async (Guid id, ExitWrite body, VisitService service, HttpRequest request) => await Run(() => service.Exit(id, body, request.Headers["Idempotency-Key"].ToString()))).RequireAuthorization(AcademyPermissions.FacilitiesManage).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/bookings/{id:guid}/player-card/resolve", async (Guid id, VisitCardWrite body, VisitService service, HttpResponse response) => { response.Headers.CacheControl = "no-store, private"; return await Run(() => service.Resolve(id, body)); }).RequireAuthorization(AcademyPermissions.FacilitiesManage).RequireRateLimiting("player-card").AddEndpointFilter<CsrfFilter>();
    }
    private static async Task<IResult> Run(Func<Task<object>> action)
    {
        try { return Results.Ok(await action()); }
        catch (FacilityRejected e) { return Results.Problem(statusCode: e.Status, title: e.Message); }
        catch (DbUpdateException e) when (e.InnerException is Npgsql.PostgresException { SqlState: "23505" }) { return Results.Problem(statusCode: 409, title: "الزيارة أو معرف العملية مسجل بالفعل؛ أعد تحميل السجل."); }
    }
}
