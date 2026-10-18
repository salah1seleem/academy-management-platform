using Academy.Api.Auth;
using Academy.Api.Facilities;
using Microsoft.EntityFrameworkCore;
namespace Academy.Api.Operations;
public static class TechnicalPromotionEndpoints
{
    public static void MapTechnicalPromotions(this WebApplication app)
    {
        var g=app.MapGroup("/api/v1/evaluations/technical-reviews/{id:guid}/promotion").RequireAuthorization(AcademyPermissions.EvaluationRead,AcademyPermissions.PeopleManage,AcademyPermissions.StructureManage);
        g.MapGet("",async(Guid id,GroupPlacementService service,HttpContext h)=>{h.Response.Headers.CacheControl="private, no-store";return await Run(()=>service.TechnicalPromotionOptions(id,h));});
        g.MapPost("/preview",async(Guid id,TechnicalPromotionWrite b,GroupPlacementService service,HttpContext h)=>await Run(()=>service.TechnicalPromotion(id,b,false,h))).AddEndpointFilter<CsrfFilter>();
        g.MapPost("",async(Guid id,TechnicalPromotionWrite b,GroupPlacementService service,HttpContext h)=>await Run(()=>service.TechnicalPromotion(id,b,true,h))).AddEndpointFilter<CsrfFilter>();
    }
    private static async Task<IResult> Run(Func<Task<object>> action)
    {
        try{return Results.Ok(await action());}catch(FacilityRejected e){return Results.Problem(statusCode:e.Status,title:e.Message);}
        catch(DbUpdateException e)when(e is DbUpdateConcurrencyException||e.InnerException is Npgsql.PostgresException{SqlState:"23505" or "23514"}){return Results.Conflict(new{message="تغير المصدر أو التسكين؛ أعد المعاينة. لم نعتمد التنفيذ."});}
    }
}
