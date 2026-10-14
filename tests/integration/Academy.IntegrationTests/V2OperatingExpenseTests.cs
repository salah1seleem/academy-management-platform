using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Finance;
using Academy.Api.Operations;
using Academy.Infrastructure.Finance;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class V2PlanPublicationTests
{
    [Fact]
    public async Task Operating_expense_demo_seed_is_opt_in_repeatable_and_tenant_scoped()
    {
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await GroupPlacementDemoSeed.Seed(db, scope.ServiceProvider.GetRequiredService<CollectionService>());
        await OperatingExpenseDemoSeed.Seed(db, DateTimeOffset.UtcNow);
        await OperatingExpenseDemoSeed.Seed(db, DateTimeOffset.UtcNow);
        Assert.Equal(1, await db.OperatingExpenses.CountAsync(x => x.Id == OperatingExpenseDemoSeed.ExpenseId && x.AcademyId == DemoSeed.NogoomAcademyId));
        Assert.Equal(1, await db.OperatingExpenseAllocations.CountAsync(x => x.ExpenseId == OperatingExpenseDemoSeed.ExpenseId));
        Assert.False(await db.OperatingExpenses.AnyAsync(x => x.Id == OperatingExpenseDemoSeed.ExpenseId && x.AcademyId == DemoSeed.FutureAcademyId));
    }

    [Fact]
    public async Task Operating_expense_posts_once_to_the_group_and_never_counts_as_a_receipt()
    {
        var f = await Fixture(); using var owner = await Staff(); using var admin = await Staff("admin.nogoom@example.test"); using var guardian = await Guardian();
        const string root = "/api/v1/finance/operating-expenses";
        var body = new OperatingExpenseWrite(f.A, OperatingExpenseScope.Group, f.Group, "Supplies", FacilityDay, FacilityDay, null, 200m, "EGP", "مستلزمات تدريب اصطناعية");
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(guardian, root + "/", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Write(admin, root + "/", body)).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Write(owner, root + "/", body with { Category = "Payroll" })).StatusCode);
        var created = await Write(owner, root + "/", body); created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var before = await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/financial?allDates=true");
        var draftUnpaid = await owner.GetFromJsonAsync<JsonElement>($"{root}/?unpaid=true&branchId={f.A}");
        Assert.DoesNotContain(draftUnpaid.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
        (await Write(owner, $"{root}/{id}/approve", new { })).EnsureSuccessStatusCode();
        var unpaid = await owner.GetFromJsonAsync<JsonElement>($"{root}/?unpaid=true&branchId={f.A}");
        Assert.Contains(unpaid.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
        var dashboard = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/owner-summary?allDates=true&branchId={f.A}");
        Assert.Contains(dashboard.GetProperty("operations").GetProperty("unpaidExpenses").EnumerateArray(), x => x.GetProperty("currency").GetString() == "EGP" && x.GetProperty("amount").GetDecimal() >= 200m);
        var ownerQueue = await owner.GetFromJsonAsync<JsonElement>($"{root}/?from={FacilityDay:yyyy-MM-dd}&to={FacilityDay:yyyy-MM-dd}&branchId={f.A}&status=Approved");
        Assert.Contains(ownerQueue.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
        var draftQueue = await owner.GetFromJsonAsync<JsonElement>($"{root}/?from={FacilityDay:yyyy-MM-dd}&to={FacilityDay:yyyy-MM-dd}&branchId={f.A}&status=Draft");
        Assert.DoesNotContain(draftQueue.GetProperty("items").EnumerateArray(), x => x.GetProperty("id").GetGuid() == id);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"{root}/?branchId={Guid.NewGuid()}")).StatusCode);
        var quoteResponse = await Write(owner, $"{root}/{id}/quote", new { }); quoteResponse.EnsureSuccessStatusCode();
        var quote = await quoteResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(f.Group, quote.GetProperty("shares")[0].GetProperty("groupId").GetGuid());
        Assert.Equal(200m, quote.GetProperty("total").GetDecimal());
        var hash = quote.GetProperty("quoteHash").GetString()!;
        var key = Guid.NewGuid().ToString();
        var posted = await ExpenseCommand(owner, $"{root}/{id}/post", new OperatingExpensePost(hash), key); posted.EnsureSuccessStatusCode();
        var replay = await ExpenseCommand(owner, $"{root}/{id}/post", new OperatingExpensePost(hash), key); replay.EnsureSuccessStatusCode();
        Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("replay").GetBoolean());
        Assert.Equal(HttpStatusCode.Conflict, (await ExpenseCommand(owner, $"{root}/{id}/post", new OperatingExpensePost(hash), Guid.NewGuid().ToString())).StatusCode);
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        Assert.Equal(OperatingExpenseStatus.Posted, (await db.OperatingExpenses.SingleAsync(x => x.Id == id)).Status);
        Assert.Equal(200m, (await db.OperatingExpenseAllocations.SingleAsync(x => x.ExpenseId == id)).Amount);
        var decisions = await owner.GetFromJsonAsync<JsonElement>($"/api/v1/reports/financial-decisions?allDates=true&branchId={f.A}");
        var expenseDecisions = decisions.GetProperty("items").EnumerateArray()
            .Where(x => x.GetProperty("kind").GetString() == "Expense" && x.GetProperty("sourceId").GetGuid() == id).ToArray();
        Assert.Equal(3, expenseDecisions.Length);
        Assert.Equal(new[] { "Approved", "Created", "Posted" }, expenseDecisions.Select(x => x.GetProperty("state").GetString()!).Order().ToArray());
        Assert.All(expenseDecisions, x => Assert.False(string.IsNullOrWhiteSpace(x.GetProperty("actorName").GetString())));
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync("/api/v1/reports/financial-decisions?allDates=true")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await guardian.GetAsync("/api/v1/reports/financial-decisions?allDates=true")).StatusCode);
        var groupResult = await owner.GetFromJsonAsync<JsonElement>($"{root}/allocations?groupId={f.Group}");
        Assert.Contains(groupResult.GetProperty("items").EnumerateArray(), x => x.GetProperty("expenseId").GetGuid() == id && x.GetProperty("amount").GetDecimal() == 200m);
        var after = await owner.GetFromJsonAsync<JsonElement>("/api/v1/reports/financial?allDates=true");
        Assert.Equal(before.GetProperty("totalAmount").ToString(), after.GetProperty("totalAmount").ToString());
        using var other = await Staff("owner.future@example.test");
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{root}/{id}")).StatusCode);
        var otherDecisions = await other.GetFromJsonAsync<JsonElement>("/api/v1/reports/financial-decisions?allDates=true");
        Assert.DoesNotContain(otherDecisions.GetProperty("items").EnumerateArray(), x => x.GetProperty("sourceId").GetGuid() == id);
    }

    [Fact]
    public async Task Facility_expense_preserves_an_unallocated_amount_and_blocks_mixed_service_loading()
    {
        await using var licensed = await FacilityLicenseScope.Begin(factory.Services);
        var f = await Fixture(); using var owner = await Staff(); var resource = await FacilityFixture(owner, branch: f.A);
        const string root = "/api/v1/finance/operating-expenses";
        var body = new OperatingExpenseWrite(f.A, OperatingExpenseScope.Resource, resource, "Water", FacilityDay, FacilityDay, null, 100m, "EGP", null);
        var created = await Write(owner, root + "/", body); created.EnsureSuccessStatusCode();
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await Write(owner, $"{root}/{id}/approve", new { })).EnsureSuccessStatusCode();
        var preview = await Write(owner, $"{root}/{id}/quote", new { }); preview.EnsureSuccessStatusCode();
        var empty = await preview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("shares")[0].GetProperty("groupId").ValueKind);
        Assert.Equal(100m, empty.GetProperty("total").GetDecimal());
        await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var source = await db.TrainingGroups.AsNoTracking().SingleAsync(g => g.Id == f.Group);
        var otherGroup = Guid.NewGuid();
        db.TrainingGroups.Add(new() { Id = otherGroup, AcademyId = DemoSeed.NogoomAcademyId, BranchId = f.A, SportId = source.SportId, AgeCategoryId = source.AgeCategoryId, TrainingLevelId = source.TrainingLevelId, ArabicName = $"مجموعة تكلفة اصطناعية {otherGroup:N}", Capacity = 10 });
        db.ResourceBookings.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, BranchId = f.A, ResourceId = resource, TrainingGroupId = f.Group, Date = FacilityDay, StartTime = new(10,0), EndTime = new(11,0), ServiceKind = "Training", ReservedSeats = 1, ApprovedCapacitySnapshot = 10, ActorUserId = f.Owner, RequestKey = Guid.NewGuid().ToString(), RequestHash = "synthetic-expense-test" });
        db.ResourceBookings.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, BranchId = f.A, ResourceId = resource, TrainingGroupId = otherGroup, Date = FacilityDay, StartTime = new(11,0), EndTime = new(12,0), ServiceKind = "Training", ReservedSeats = 1, ApprovedCapacitySnapshot = 10, ActorUserId = f.Owner, RequestKey = Guid.NewGuid().ToString(), RequestHash = "synthetic-expense-other-group" });
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await ExpenseCommand(owner, $"{root}/{id}/post", new OperatingExpensePost(empty.GetProperty("quoteHash").GetString()!), Guid.NewGuid().ToString())).StatusCode);
        var changedResponse = await Write(owner, $"{root}/{id}/quote", new { }); changedResponse.EnsureSuccessStatusCode();
        var changed = await changedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, changed.GetProperty("shares").GetArrayLength());
        Assert.All(changed.GetProperty("shares").EnumerateArray(), s => Assert.Equal(50m, s.GetProperty("amount").GetDecimal()));
        (await ExpenseCommand(owner, $"{root}/{id}/post", new OperatingExpensePost(changed.GetProperty("quoteHash").GetString()!), Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        Assert.Equal(100m, await db.OperatingExpenseAllocations.Where(x => x.ExpenseId == id).SumAsync(x => x.Amount));
        var groupOnly = await owner.GetFromJsonAsync<JsonElement>($"{root}/allocations?groupId={f.Group}");
        var stored = groupOnly.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("expenseId").GetGuid() == id);
        Assert.Equal(50m, stored.GetProperty("amount").GetDecimal());
        Assert.Equal(100m, stored.GetProperty("expenseAmount").GetDecimal());

        var mixedResponse = await Write(owner, root + "/", body with { Category = "Heating" }); mixedResponse.EnsureSuccessStatusCode();
        var mixedId = (await mixedResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await Write(owner, $"{root}/{mixedId}/approve", new { })).EnsureSuccessStatusCode();
        var trainingOnlyQuoteResponse = await Write(owner, $"{root}/{mixedId}/quote", new { }); trainingOnlyQuoteResponse.EnsureSuccessStatusCode();
        var trainingOnlyQuote = await trainingOnlyQuoteResponse.Content.ReadFromJsonAsync<JsonElement>();
        db.ResourceBookings.Add(new() { Id = Guid.NewGuid(), AcademyId = DemoSeed.NogoomAcademyId, BranchId = f.A, ResourceId = resource, Date = FacilityDay, StartTime = new(12,0), EndTime = new(13,0), ServiceKind = "DayPass", ReservedSeats = 1, ApprovedCapacitySnapshot = 10, ActorUserId = f.Owner, RequestKey = Guid.NewGuid().ToString(), RequestHash = "synthetic-mixed-cost-test" });
        await db.SaveChangesAsync();
        Assert.Equal(HttpStatusCode.Conflict, (await ExpenseCommand(owner, $"{root}/{mixedId}/post", new OperatingExpensePost(trainingOnlyQuote.GetProperty("quoteHash").GetString()!), Guid.NewGuid().ToString())).StatusCode);
        var mixedQuoteResponse = await Write(owner, $"{root}/{mixedId}/quote", new { }); mixedQuoteResponse.EnsureSuccessStatusCode();
        var mixedQuote = await mixedQuoteResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, mixedQuote.GetProperty("shares").GetArrayLength());
        Assert.Equal(100m, mixedQuote.GetProperty("total").GetDecimal());
        Assert.Contains(mixedQuote.GetProperty("shares").EnumerateArray(), x => x.GetProperty("serviceKind").GetString() == "DayPass" && x.GetProperty("groupId").ValueKind == JsonValueKind.Null && x.GetProperty("amount").GetDecimal() == 33.33m);
        (await ExpenseCommand(owner, $"{root}/{mixedId}/post", new OperatingExpensePost(mixedQuote.GetProperty("quoteHash").GetString()!), Guid.NewGuid().ToString())).EnsureSuccessStatusCode();
        Assert.Equal(100m, await db.OperatingExpenseAllocations.Where(x => x.ExpenseId == mixedId).SumAsync(x => x.Amount));
        Assert.Equal(33.33m, (await db.OperatingExpenseAllocations.SingleAsync(x => x.ExpenseId == mixedId && x.ServiceKind == "DayPass")).Amount);
        var mixedGroup = await owner.GetFromJsonAsync<JsonElement>($"{root}/allocations?groupId={f.Group}");
        Assert.InRange(mixedGroup.GetProperty("items").EnumerateArray().Single(x => x.GetProperty("expenseId").GetGuid() == mixedId).GetProperty("amount").GetDecimal(), 33.33m, 33.34m);
    }

    private static async Task<HttpResponseMessage> ExpenseCommand(HttpClient client, string path, object body, string key)
    {
        var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = JsonContent.Create(body) };
        request.Headers.Add("X-CSRF-TOKEN", csrf.GetProperty("token").GetString());
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }
}
