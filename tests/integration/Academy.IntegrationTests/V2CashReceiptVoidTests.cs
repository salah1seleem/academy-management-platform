using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Infrastructure.Attendance;
using Academy.Infrastructure.Facilities;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Subscriptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Academy.IntegrationTests;

public sealed partial class V2ReceivablesTests
{
    private static string VoidRoot(Guid receipt)=>$"{CashRoot}/receipts/{receipt}";
    private async Task<Guid> VoidReceiptId(Guid collection)
    {
        await using var s=factory.Services.CreateAsyncScope();return await s.ServiceProvider.GetRequiredService<FoundationDbContext>().Receipts.Where(r=>r.CollectionId==collection).Select(r=>r.Id).SingleAsync();
    }
    private async Task<CashReceiptVoidWrite> VoidQuote(HttpClient owner,Guid receipt,Guid session,string? reason=null)
    {
        var b=new CashReceiptVoidWrite(session,reason);var r=await Post(owner,VoidRoot(receipt)+"/void-quote",b);r.EnsureSuccessStatusCode();
        return b with {QuoteHash=(await r.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quoteHash").GetString()};
    }
    [Fact]
    public async Task Cash_void_reverses_once_cancels_only_linked_period_preserves_receipt_and_replays_after_close()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashRefundFixture(owner);var id=await VoidReceiptId(f.Collection);var body=await VoidQuote(owner,id,f.Session);var key=Guid.NewGuid().ToString();
        var original=await owner.GetFromJsonAsync<JsonElement>($"/api/v1/subscriptions/receipts/{id}");Assert.True(original.GetProperty("canReviewVoid").GetBoolean());
        var results=await Task.WhenAll(Post(owner,VoidRoot(id)+"/void",body,key),Post(owner,VoidRoot(id)+"/void",body,key));Assert.All(results,r=>r.EnsureSuccessStatusCode());
        var saved=(await results[0].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();Assert.Equal(saved,(await results[1].Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid());
        await using var s=factory.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var row=await db.CashReceiptVoids.SingleAsync(v=>v.ReceiptId==id);Assert.Equal(saved,row.Id);Assert.Equal(SubscriptionPeriodStatus.Cancelled,(await db.SubscriptionPeriods.SingleAsync(p=>p.Id==row.SubscriptionPeriodId)).Status);
        Assert.Equal(f.Amount,(await db.CollectionRefunds.SingleAsync(r=>r.Id==row.RefundId)).Amount);Assert.Single(await db.CashRefundPostings.Where(p=>p.SessionId==f.Session).ToArrayAsync());
        Assert.Equal(SubscriptionAdjustmentType.Cancelled,(await db.SubscriptionAdjustments.SingleAsync(a=>a.Id==row.AdjustmentId)).AdjustmentType);
        Assert.True((await db.Players.SingleAsync(p=>p.Id==f.Player)).IsActive);Assert.False(await db.ReceivableMovements.AnyAsync(m=>m.CollectionId==f.Collection));
        var detail=await owner.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{f.Session}");Assert.Equal("100.00",detail.GetProperty("balance").GetString());Assert.Single(detail.GetProperty("receipts").EnumerateArray());
        (await Post(owner,$"{CashRoot}/sessions/{f.Session}/close",new{countedAmount="100.00"})).EnsureSuccessStatusCode();
        Assert.True((await (await Post(owner,VoidRoot(id)+"/void",body,key)).Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replay").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,VoidRoot(id)+"/void",body with {Reason="تغيير"},key)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,VoidRoot(id)+"/void",body)).StatusCode);
        using var guardian=await Guardian();var read=await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/subscriptions/receipts/{id}");Assert.True(read.GetProperty("isVoided").GetBoolean());
        foreach(var field in new[]{"receiptNumber","amount","paidAtUtc","playerNameSnapshot"}) Assert.Equal(original.GetProperty(field).ToString(),read.GetProperty(field).ToString());
        var day=DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(row.CreatedAtUtc,TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo")).DateTime);
        var code=await db.Players.Where(p=>p.Id==f.Player).Select(p=>p.PlayerCode).SingleAsync();var filter=$"from={day:yyyy-MM-dd}&to={day:yyyy-MM-dd}&search={code}";
        var report=await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/financial?"+filter);Assert.Equal(-f.Amount,report.GetProperty("netAmount").GetDecimal());
        Assert.Equal("Void",Assert.Single(report.GetProperty("items").EnumerateArray()).GetProperty("collectionType").GetString());
        Assert.Contains("عكس إيصال نقدي",await owner.GetStringAsync("/api/v1/reports/financial/export?"+filter));
    }
    [Fact]
    public async Task Cash_void_owner_only_tenant_csrf_and_review_are_enforced()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);using var admin=await Staff(DemoSeed.AdminEmail);using var guardian=await Guardian();using var other=await Staff(DemoSeed.FutureOwnerEmail);
        var f=await CashRefundFixture(owner);var id=await VoidReceiptId(f.Collection);var body=await VoidQuote(owner,id,f.Session);
        foreach(var client in new[]{admin,guardian}) {
            Assert.Equal(HttpStatusCode.Forbidden,(await client.GetAsync(VoidRoot(id)+"/void-options")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await Post(client,VoidRoot(id)+"/void-quote",body)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,(await Post(client,VoidRoot(id)+"/void",body)).StatusCode);
        }
        Assert.Equal(HttpStatusCode.NotFound,(await Post(other,VoidRoot(id)+"/void",body)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await owner.GetAsync(VoidRoot(Guid.NewGuid())+"/void-options")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync(VoidRoot(id)+"/void",body)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Post(owner,VoidRoot(id)+"/void",body with {QuoteHash=null})).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Post(owner,VoidRoot(id)+"/void-quote",body with {Reason=new string('ا',501)})).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,VoidRoot(id)+"/void",body with {Reason="سبب آخر"})).StatusCode);
    }
    [Theory]
    [InlineData("partial")][InlineData("allocated")][InlineData("attendance")][InlineData("consumed")][InlineData("cancelled")][InlineData("frozen")][InlineData("entry")][InlineData("compensation")]
    public async Task Cash_void_refuses_used_adjusted_or_previously_applied_source(string kind)
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashRefundFixture(owner);var id=await VoidReceiptId(f.Collection);var body=await VoidQuote(owner,id,f.Session);
        if(kind=="partial") (await Post(owner,RefundRoot(f.Player),await CashRefundQuote(owner,f.Player,f.Collection,f.Session,10))).EnsureSuccessStatusCode();
        else if(kind=="allocated") (await Move(owner,await Charge(owner,f.Enrollment,100),"PaymentAllocation",10,f.Collection)).EnsureSuccessStatusCode();
        else {
            await using var s=factory.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();var p=await db.SubscriptionPeriods.SingleAsync(p=>p.CollectionId==f.Collection);
            if(kind is "attendance" or "entry" or "compensation") {
                var e=await db.SportEnrollments.Include(e=>e.TrainingGroup).SingleAsync(e=>e.Id==f.Enrollment);
                var session=await db.TrainingSessions.FirstOrDefaultAsync(s=>s.TrainingGroupId==e.TrainingGroupId && s.SessionDate==p.StartDate && s.StartTime==new TimeOnly(10,0));
                if(session is null){session=new TrainingSession {Id=Guid.NewGuid(),AcademyId=e.AcademyId,TrainingGroupId=e.TrainingGroupId!.Value,BranchId=e.BranchId,SportId=e.SportId,AgeCategoryId=e.TrainingGroup!.AgeCategoryId,SessionDate=p.StartDate,StartTime=new(10,0),EndTime=new(11,0),CreatedByUserId=p.CreatedByUserId};db.TrainingSessions.Add(session);}
                if(kind=="attendance") db.PlayerAttendances.Add(new(){Id=Guid.NewGuid(),AcademyId=e.AcademyId,TrainingSessionId=session.Id,TrainingGroupId=session.TrainingGroupId,SportEnrollmentId=e.Id,Status=AttendanceStatus.Present,RecordedByUserId=session.CreatedByUserId});
                else if(kind=="compensation") db.CompensationGrants.Add(new(){Id=Guid.NewGuid(),AcademyId=e.AcademyId,SportEnrollmentId=e.Id,BranchId=e.BranchId,SportId=e.SportId,SourceSessionId=session.Id,SourceGroupId=session.TrainingGroupId,SourcePeriodId=p.Id,Cause=CompensationCause.ApprovedAbsence,Unit=CompensationUnit.Session,Units=1,ValidFrom=p.StartDate,ValidTo=p.StartDate.AddDays(30),ActorUserId=p.CreatedByUserId,RequestKey=Guid.NewGuid().ToString(),RequestHash=new string('a',64),Snapshot="{}"});
                else {
                    var at=DateTimeOffset.UtcNow;
                    var facility=new Facility {Id=Guid.NewGuid(),AcademyId=e.AcademyId,BranchId=e.BranchId,Name="مرفق عكس اصطناعي "+Guid.NewGuid().ToString("N")[..10]};
                    var space=new FacilitySpace {Id=Guid.NewGuid(),AcademyId=e.AcademyId,FacilityId=facility.Id,BranchId=e.BranchId,SportId=e.SportId,Kind=FacilitySpaceKind.Pitch,Name="ملعب اصطناعي"};
                    var resource=new FacilityResource {Id=Guid.NewGuid(),AcademyId=e.AcademyId,FacilitySpaceId=space.Id,BranchId=e.BranchId,Name="مساحة اختبار",Capacity=1,OpensAt=new(8,0),ClosesAt=new(22,0),OperatingDays=[0,1,2,3,4,5,6],ServiceKinds=["Training"],ApprovedByUserId=p.CreatedByUserId,ApprovedAtUtc=at,ApprovalEvidence="اصطناعي"};
                    var booking=new ResourceBooking {Id=Guid.NewGuid(),AcademyId=e.AcademyId,ResourceId=resource.Id,BranchId=e.BranchId,TrainingGroupId=session.TrainingGroupId,TrainingSessionId=session.Id,Date=p.StartDate,StartTime=session.StartTime,EndTime=session.EndTime,ServiceKind="Training",ReservedSeats=1,ApprovedCapacitySnapshot=1,State=ResourceBookingState.Reserved,ActorUserId=p.CreatedByUserId,RequestKey=Guid.NewGuid().ToString(),RequestHash=new string('a',64)};
                    db.Facilities.Add(facility);db.FacilitySpaces.Add(space);db.FacilityResources.Add(resource);db.ResourceBookings.Add(booking);
                    db.AccessEntitlements.Add(new(){Id=Guid.NewGuid(),AcademyId=e.AcademyId,BranchId=e.BranchId,ResourceId=resource.Id,ResourceBookingId=booking.Id,PersonKind=EntryPersonKind.Participant,PlayerId=e.PlayerId,SportEnrollmentId=e.Id,SubscriptionPeriodId=p.Id,BookingDate=p.StartDate,ValidFromUtc=at,ValidToUtc=at.AddHours(1),AuthorizedByUserId=p.CreatedByUserId,Basis="TrainingSubscription"});
                }
            } else if(kind=="consumed") {p.InitialSessions=5;p.RemainingSessions=4;}
            else p.Status=kind=="cancelled"?SubscriptionPeriodStatus.Cancelled:SubscriptionPeriodStatus.Frozen;
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,VoidRoot(id)+"/void",body)).StatusCode);
        await using var check=factory.Services.CreateAsyncScope();Assert.False(await check.ServiceProvider.GetRequiredService<FoundationDbContext>().CashReceiptVoids.AnyAsync(v=>v.ReceiptId==id));
    }
    [Fact]
    public async Task Cash_void_stale_period_and_closed_or_revoked_shift_leave_every_source_unchanged()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashRefundFixture(owner);var id=await VoidReceiptId(f.Collection);var body=await VoidQuote(owner,id,f.Session);
        await using var s=factory.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();var p=await db.SubscriptionPeriods.SingleAsync(p=>p.CollectionId==f.Collection);p.UpdatedAtUtc=DateTimeOffset.UtcNow;await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,VoidRoot(id)+"/void",body)).StatusCode);body=await VoidQuote(owner,id,f.Session);
        (await CashAssign(owner,f.Register,f.Member,false)).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Forbidden,(await Post(owner,VoidRoot(id)+"/void",body)).StatusCode);(await CashAssign(owner,f.Register,f.Member)).EnsureSuccessStatusCode();
        (await Post(owner,$"{CashRoot}/sessions/{f.Session}/close",new{countedAmount=CashRegisterService.Money(100+f.Amount)})).EnsureSuccessStatusCode();Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,VoidRoot(id)+"/void",body)).StatusCode);
        Assert.False(await db.CollectionRefunds.AnyAsync(r=>r.CollectionId==f.Collection));Assert.False(await db.SubscriptionAdjustments.AnyAsync(a=>a.SubscriptionPeriodId==p.Id));
        Assert.NotEqual(SubscriptionPeriodStatus.Cancelled,(await db.SubscriptionPeriods.AsNoTracking().SingleAsync(x=>x.Id==p.Id)).Status);
    }
    [Fact]
    public async Task Cash_void_parallel_distinct_keys_and_close_race_are_atomic()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashRefundFixture(owner);var id=await VoidReceiptId(f.Collection);var body=await VoidQuote(owner,id,f.Session);
        var results=await Task.WhenAll(Post(owner,VoidRoot(id)+"/void",body),Post(owner,VoidRoot(id)+"/void",body));Assert.Single(results,r=>r.IsSuccessStatusCode);Assert.Single(results,r=>r.StatusCode==HttpStatusCode.Conflict);
        var second=await CashRefundFixture(owner);id=await VoidReceiptId(second.Collection);body=await VoidQuote(owner,id,second.Session);
        var race=await Task.WhenAll(Post(owner,VoidRoot(id)+"/void",body),Post(owner,$"{CashRoot}/sessions/{second.Session}/close",new{countedAmount=CashRegisterService.Money(100+second.Amount)}));race[1].EnsureSuccessStatusCode();Assert.True(race[0].IsSuccessStatusCode||race[0].StatusCode==HttpStatusCode.Conflict);
        var detail=await owner.GetFromJsonAsync<JsonElement>($"{CashRoot}/sessions/{second.Session}");Assert.Equal(detail.GetProperty("balance").GetString(),detail.GetProperty("session").GetProperty("expectedAmount").GetString());
        await using var s=factory.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();var succeeded=race[0].IsSuccessStatusCode;
        Assert.Equal(succeeded,await db.CashReceiptVoids.AnyAsync(v=>v.ReceiptId==id));Assert.Equal(succeeded,await db.CollectionRefunds.AnyAsync(r=>r.CollectionId==second.Collection));Assert.Equal(succeeded,(await db.SubscriptionPeriods.SingleAsync(p=>p.CollectionId==second.Collection)).Status==SubscriptionPeriodStatus.Cancelled);
    }
    [Fact]
    public async Task Cash_void_does_not_cancel_another_paid_period_or_spend_an_empty_shift()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashRefundFixture(owner);var id=await VoidReceiptId(f.Collection);
        var secondQuote=await CashQuote(owner,f.Session,f.Enrollment);(await CashCollect(owner,f.Session,f.Enrollment,secondQuote.GetProperty("quoteHash").GetString()!)).EnsureSuccessStatusCode();
        (await Post(owner,VoidRoot(id)+"/void",await VoidQuote(owner,id,f.Session))).EnsureSuccessStatusCode();
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var periods=await db.SubscriptionPeriods.Where(p=>p.SportEnrollmentId==f.Enrollment).ToArrayAsync();
        Assert.Equal(2,periods.Length);Assert.Single(periods,p=>p.Status==SubscriptionPeriodStatus.Cancelled);Assert.Single(periods,p=>p.Status!=SubscriptionPeriodStatus.Cancelled);
        var receipt=await db.Receipts.Where(r=>r.SportEnrollmentId==f.Enrollment&&r.Id!=id).Select(r=>r.Id).SingleAsync();
        (await Post(owner,$"{CashRoot}/sessions/{f.Session}/close",new{countedAmount=CashRegisterService.Money(100+f.Amount)})).EnsureSuccessStatusCode();var empty=await CashOpen(owner,f.Register,"0");
        Assert.Equal(HttpStatusCode.Conflict,(await Post(owner,VoidRoot(receipt)+"/void-quote",new CashReceiptVoidWrite(empty))).StatusCode);
        Assert.Single(await db.CashReceiptVoids.Where(v=>v.ReceiptId==id||v.ReceiptId==receipt).ToArrayAsync());
    }
    [Fact]
    public async Task Cash_void_database_rejects_linking_a_reversal_to_an_unrelated_receipt()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashRefundFixture(owner);var id=await VoidReceiptId(f.Collection);(await Post(owner,VoidRoot(id)+"/void",await VoidQuote(owner,id,f.Session))).EnsureSuccessStatusCode();
        var other=await CashRefundFixture(owner);var otherReceipt=await VoidReceiptId(other.Collection);
        await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();var row=await db.CashReceiptVoids.AsNoTracking().SingleAsync(v=>v.ReceiptId==id);
        row.Id=Guid.NewGuid();row.ReceiptId=otherReceipt;row.RequestKey=Guid.NewGuid().ToString();db.CashReceiptVoids.Add(row);
        var error=await Assert.ThrowsAsync<DbUpdateException>(()=>db.SaveChangesAsync());Assert.Equal("23514",Assert.IsType<PostgresException>(error.InnerException).SqlState);
    }
    [Fact]
    public async Task Cash_void_and_cancellation_audit_immutable_and_period_cannot_be_reactivated()
    {
        using var owner=await Staff(DemoSeed.OwnerEmail);var f=await CashRefundFixture(owner);var id=await VoidReceiptId(f.Collection);var b=await VoidQuote(owner,id,f.Session,new string('ا',450));(await Post(owner,VoidRoot(id)+"/void",b)).EnsureSuccessStatusCode();
        await using var s=factory.Services.CreateAsyncScope();var db=s.ServiceProvider.GetRequiredService<FoundationDbContext>();var row=await db.CashReceiptVoids.SingleAsync(v=>v.ReceiptId==id);
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"CashReceiptVoids\" WHERE \"Id\"={row.Id}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"CashReceiptVoids\" SET \"Snapshot\"='{{}}' WHERE \"Id\"={row.Id}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"SubscriptionPeriods\" SET \"Status\"='Active' WHERE \"Id\"={row.SubscriptionPeriodId}"))).SqlState);
        Assert.Equal("23514",(await Assert.ThrowsAsync<PostgresException>(()=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"SubscriptionAdjustments\" SET \"Reason\"='x' WHERE \"Id\"={row.AdjustmentId}"))).SqlState);
        row.Snapshot="{}";await Assert.ThrowsAsync<InvalidOperationException>(()=>db.SaveChangesAsync());
    }
}
