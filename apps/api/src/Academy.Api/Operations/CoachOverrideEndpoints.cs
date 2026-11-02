using Academy.Api.Auth;
using Academy.Api.Facilities;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Operations;
public static class CoachOverrideEndpoints
{
    public static void MapCoachOverrides(this WebApplication app)
    {
        var api=app.MapGroup("/api/v1/manage/structure/sessions/{id:guid}/coach-override").RequireAuthorization(AcademyPermissions.StructureManage);
        api.MapGet("/",async(Guid id,CoachOverrideService service,HttpResponse response)=>{response.Headers.CacheControl="no-store, private";return await Run(()=>service.Read(id));});
        api.MapPost("/preview",async(Guid id,CoachOverrideWrite body,CoachOverrideService service)=>await Run(()=>service.Preview(id,body))).AddEndpointFilter<CsrfFilter>();
        api.MapPut("/",async(Guid id,CoachOverrideWrite body,CoachOverrideService service,HttpRequest request)=>await Run(()=>service.Apply(id,body,request.Headers["Idempotency-Key"].ToString()))).AddEndpointFilter<CsrfFilter>();
    }
    private static async Task<IResult> Run(Func<Task<object>> action)
    {
        try{return Results.Ok(await action());}
        catch(FacilityRejected e){return Results.Problem(statusCode:e.Status,title:e.Message);}
        catch(DbUpdateException e)when(e.InnerException is Npgsql.PostgresException{SqlState:"23505"}){return Results.Problem(statusCode:409,title:"تغير قرار البديل أو تكرر معرف العملية؛ أعد تحميل الحصة.");}
    }
}
