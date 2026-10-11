using Academy.Api.Auth;
using Academy.Api.Facilities;

namespace Academy.Api.Slice4;
public static class PlayerCardEndpoints
{
    public static void MapPlayerCards(this WebApplication app)
    {
        var cards = app.MapGroup("/api/v1/attendance/players/{playerId:guid}/card").RequireAuthorization(AcademyPermissions.PeopleManage, AcademyPermissions.AttendanceRead);
        cards.MapGet("", async (Guid playerId, PlayerCardService service, HttpResponse response) => { response.Headers.CacheControl = "no-store, private"; return await Run(() => service.Read(playerId, false)); });
        cards.MapPost("", async (Guid playerId, PlayerCardWrite body, PlayerCardService service, HttpRequest request, HttpResponse response) => { response.Headers.CacheControl = "no-store, private"; return await Run(() => service.Issue(playerId, body, request.Headers["Idempotency-Key"].ToString())); }).RequireAuthorization(AcademyPermissions.AttendanceManage).AddEndpointFilter<CsrfFilter>();
        cards.MapPut("/revoke", async (Guid playerId, PlayerCardWrite body, PlayerCardService service) => await Run(() => service.Revoke(playerId, body))).RequireAuthorization(AcademyPermissions.AttendanceManage).AddEndpointFilter<CsrfFilter>();
        app.MapGet("/api/v1/guardian/children/{playerId:guid}/card", async (Guid playerId, PlayerCardService service, HttpResponse response) => { response.Headers.CacheControl = "no-store, private"; return await Run(() => service.Read(playerId, true)); }).RequireAuthorization(AcademyPermissions.GuardianChildrenRead, AcademyPermissions.GuardianAttendanceRead);
        app.MapPost("/api/v1/attendance/sessions/{id:guid}/player-card/resolve", async (Guid id, PlayerCardResolve body, PlayerCardService service, HttpResponse response) => { response.Headers.CacheControl = "no-store, private"; return await Run(() => service.Resolve(id, body)); }).RequireAuthorization(AcademyPermissions.AttendanceManage).RequireRateLimiting("player-card").AddEndpointFilter<CsrfFilter>();
    }
    private static async Task<IResult> Run(Func<Task<object>> action)
    {
        try { return Results.Ok(await action()); } catch (FacilityRejected e) { return Results.Problem(statusCode: e.Status, title: e.Message); }
    }
}
