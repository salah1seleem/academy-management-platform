using Academy.Api.Auth;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Facilities;
public static class DayPassEndpoints
{
    public static void MapDayPass(this WebApplication app)
    {
        var guardian=app.MapGroup("/api/v1/guardian/day-pass").RequireAuthorization(AcademyPermissions.GuardianChildrenRead);
        guardian.MapGet("/options",async(DayPassService service)=>await Run(service.Options));
        guardian.MapPost("/quote",async(DayPassChoice body,DayPassService service)=>await Run(()=>service.Quote(body))).AddEndpointFilter<CsrfFilter>();
        guardian.MapPost("/orders",async(DayPassOrder body,DayPassService service,HttpRequest request)=>await Run(()=>service.Order(body,request.Headers["Idempotency-Key"].ToString()))).AddEndpointFilter<CsrfFilter>();
        guardian.MapGet("/orders",async(DayPassService service)=>await Run(service.Mine));
        guardian.MapPost("/orders/{id:guid}/proof",async(Guid id,DayPassProof body,DayPassService service)=>await Run(()=>service.Proof(id,body))).AddEndpointFilter<CsrfFilter>();
        var staff=app.MapGroup("/api/v1/facilities/day-pass").RequireAuthorization(AcademyPermissions.FacilitiesRead);
        staff.MapGet("/review",async(DayPassService service)=>await Run(service.Queue));
        staff.MapPost("/{id:guid}/review",async(Guid id,DayPassReview body,DayPassService service)=>await Run(()=>service.Review(id,body))).RequireAuthorization(AcademyPermissions.FacilitiesManage).AddEndpointFilter<CsrfFilter>();
    }
    private static async Task<IResult> Run(Func<Task<object>> work)
    {
        try{return Results.Ok(await work());}
        catch(FacilityRejected e){return Results.Problem(statusCode:e.Status,title:e.Message);}
        catch(DbUpdateException e) when(e.InnerException is Npgsql.PostgresException {SqlState:"23505"}){return Results.Problem(statusCode:409,title:"الطلب مسجل بالفعل؛ أعد تحميل القائمة.");}
    }
}
