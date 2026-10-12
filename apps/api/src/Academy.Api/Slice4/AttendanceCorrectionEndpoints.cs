using Academy.Api.Auth;
using Academy.Api.Facilities;
using Microsoft.EntityFrameworkCore;

namespace Academy.Api.Slice4;
public static class AttendanceCorrectionEndpoints
{
    public static void MapAttendanceCorrections(this WebApplication app)
    {
        var api=app.MapGroup("/api/v1/attendance").RequireAuthorization(AcademyPermissions.AttendanceRead);
        api.MapGet("/sessions/{id:guid}/correction-context",async(Guid id,AttendanceCorrectionService service)=>await Run(async()=>await service.Context(id)));
        api.MapPost("/sessions/{id:guid}/corrections",async(Guid id,AttendanceCorrectionWrite body,HttpRequest request,AttendanceCorrectionService service)=>await Run(()=>service.Request(id,body,request.Headers["Idempotency-Key"].ToString()))).RequireAuthorization(AcademyPermissions.AttendanceManage).AddEndpointFilter<CsrfFilter>();
        api.MapGet("/corrections",async(AttendanceCorrectionService service)=>await Run(()=>service.Read()));
        api.MapGet("/corrections/{id:guid}",async(Guid id,AttendanceCorrectionService service)=>await Run(()=>service.Read(id)));
        api.MapPost("/corrections/{id:guid}/approve",async(Guid id,AttendanceCorrectionDecision body,AttendanceCorrectionService service)=>await Run(()=>service.Decide(id,body,true))).RequireAuthorization(AcademyPermissions.AttendanceManage).AddEndpointFilter<CsrfFilter>();
        api.MapPost("/corrections/{id:guid}/reject",async(Guid id,AttendanceCorrectionDecision body,AttendanceCorrectionService service)=>await Run(()=>service.Decide(id,body,false))).RequireAuthorization(AcademyPermissions.AttendanceManage).AddEndpointFilter<CsrfFilter>();
    }
    private static async Task<IResult> Run(Func<Task<object>> call)
    {
        try{return Results.Ok(await call());}catch(FacilityRejected e){return Results.Problem(statusCode:e.Status,title:e.Message);}catch(AttendanceConflictException e){return Results.Conflict(new{message=e.Message});}catch(AttendanceValidationException e){return Results.UnprocessableEntity(new{message=e.Message});}catch(AttendanceNotFoundException){return Results.NotFound();}catch(DbUpdateConcurrencyException){return Results.Conflict(new{message="تغير الطلب أو الحضور أثناء المراجعة؛ لم يحفظ التصحيح."});}
    }
}
