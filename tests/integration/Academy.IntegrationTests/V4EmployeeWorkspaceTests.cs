using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    // Final V4 suite only: isolated synthetic database, no manual-demo mutation.
    [Fact] public async Task V4_employee_month_position_matches_payroll_and_manual_addition_is_sensitive_and_idempotent()
    {
        using var owner=await Staff();using var admin=await Staff(DemoSeed.AdminEmail);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var branch=await db.Branches.Where(b=>b.AcademyId==DemoSeed.NogoomAcademyId&&b.IsActive).Select(b=>b.Id).FirstAsync();
        var creation=await Send(owner,"/api/v1/manage/employees/",new{name="موظف اصطناعي V4 "+Guid.NewGuid(),phone="010999"+Random.Shared.Next(10000,99999),jobTitle="منسق تجريبي",branchId=branch,monthlySalary=5000m,contractStart="2026-11-16",contractEnd=(string?)null,membershipId=(Guid?)null,photoAssetId=(Guid?)null,isActive=true});Assert.Equal(HttpStatusCode.Created,creation.StatusCode);var id=(await creation.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var addition=new{employeeId=id,kind="Addition",amount=150m,dueDate="2026-11-20",reason="إضافة تجريبية معتمدة"};var key=Guid.NewGuid().ToString();
        Assert.Equal(HttpStatusCode.Forbidden,(await Send(admin,"/api/v1/manage/payroll/adjustments",addition,key:key)).StatusCode);
        Assert.Equal(HttpStatusCode.Created,(await Send(owner,"/api/v1/manage/payroll/adjustments",addition,key:key)).StatusCode);Assert.Equal(HttpStatusCode.OK,(await Send(owner,"/api/v1/manage/payroll/adjustments",addition,key:key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Send(owner,"/api/v1/manage/payroll/adjustments",new{addition.employeeId,kind="Deduction",addition.amount,addition.dueDate,addition.reason},key:key)).StatusCode);
        Assert.Equal(HttpStatusCode.Created,(await Send(owner,"/api/v1/manage/payroll/adjustments",new{employeeId=id,kind="Deduction",amount=50m,dueDate="2026-11-20",reason="خصم تجريبي"},key:Guid.NewGuid().ToString())).StatusCode);
        var list=await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/employees/?month=2026-11-01");var employee=list.GetProperty("items").EnumerateArray().Single(e=>e.GetProperty("id").GetGuid()==id);var report=await owner.GetFromJsonAsync<JsonElement>("/api/v1/manage/payroll/?month=2026-11-01");var row=report.GetProperty("items").EnumerateArray().Single(e=>e.GetProperty("employeeId").GetGuid()==id);
        foreach(var field in new[]{"baseDue","deductions","additions","netDue"})Assert.Equal(row.GetProperty(field).GetDecimal(),employee.GetProperty("financial").GetProperty(field).GetDecimal());
        Assert.Equal(2500m,row.GetProperty("baseDue").GetDecimal());Assert.Equal(2600m,row.GetProperty("netDue").GetDecimal());Assert.Equal(1,await db.PayrollAdjustments.CountAsync(a=>a.EmployeeId==id&&a.Kind=="Addition"));
        using var foreign=await Staff(DemoSeed.FutureOwnerEmail);Assert.Equal(HttpStatusCode.NotFound,(await foreign.GetAsync($"/api/v1/manage/employees/{id}")).StatusCode);
    }
}
