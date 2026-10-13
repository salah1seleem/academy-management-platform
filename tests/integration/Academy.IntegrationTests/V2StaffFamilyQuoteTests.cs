using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Api.Operations;
using Academy.Api.Slice2;
using Academy.Api.Slice3;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class SubscriptionPaymentTests
{
    private const string StaffFamilyQuotes = "/api/v1/manage/family-renewals/quotes";
    private async Task<string> FamilyPhone(FamilyFixture f) { await using var s = factory.Services.CreateAsyncScope(); return (await s.ServiceProvider.GetRequiredService<FoundationDbContext>().Users.SingleAsync(u => u.Id == f.User)).PhoneNumber!; }
    [Fact]
    public async Task Staff_family_quote_finds_trusted_children_and_prices_without_writing_or_reusing_guardian_consent()
    {
        await using var f = await NewFamily(); using var owner = await InboxStaff(DemoSeed.OwnerEmail); var phone = await FamilyPhone(f); await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var before = (await db.FamilyRenewalRequests.CountAsync(), await db.PaymentRequests.CountAsync(), await db.Collections.CountAsync(), await db.Receipts.CountAsync(), await db.SubscriptionPeriods.CountAsync());
        var search = await owner.GetFromJsonAsync<JsonElement>($"{StaffFamilyQuotes}/families?search={Uri.EscapeDataString(phone)}"); Assert.Equal(f.Guardian, Assert.Single(search.GetProperty("items").EnumerateArray()).GetProperty("id").GetGuid());
        var options = await owner.GetFromJsonAsync<JsonElement>($"{StaffFamilyQuotes}/{f.Guardian}/options"); Assert.Equal(2, options.GetProperty("items").GetArrayLength());
        var response = await Post(owner, $"{StaffFamilyQuotes}/{f.Guardian}/quote", new FamilyRenewalWrite(f.Choices), await Csrf(owner)); response.EnsureSuccessStatusCode(); var quote = await response.Content.ReadFromJsonAsync<JsonElement>();
        var guardian = await Post(f.Client, FamilyQuotes + "/quote", new FamilyRenewalWrite(f.Choices), await Csrf(f.Client)); guardian.EnsureSuccessStatusCode(); var own = await guardian.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(Decimal(own, "totalAmount"), Decimal(quote, "totalAmount")); Assert.NotEqual(own.GetProperty("quoteHash").GetString(), quote.GetProperty("quoteHash").GetString());
        var repeat = await Post(owner, $"{StaffFamilyQuotes}/{f.Guardian}/quote", new FamilyRenewalWrite(f.Choices.Reverse().ToArray()), await Csrf(owner)); repeat.EnsureSuccessStatusCode(); Assert.Equal(quote.GetProperty("quoteHash").GetString(), (await repeat.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("quoteHash").GetString());
        Assert.Equal(before, (await db.FamilyRenewalRequests.CountAsync(), await db.PaymentRequests.CountAsync(), await db.Collections.CountAsync(), await db.Receipts.CountAsync(), await db.SubscriptionPeriods.CountAsync()));
    }
    [Fact]
    public async Task Staff_family_quote_rejects_foreign_family_unlinked_lines_roles_missing_csrf_and_invalid_search()
    {
        await using var f = await NewFamily(); await using var otherFamily = await NewFamily(); using var owner = await InboxStaff(DemoSeed.OwnerEmail); using var foreign = await InboxStaff(DemoSeed.FutureOwnerEmail); using var coach = await InboxStaff(DemoSeed.CoachEmail); using var anonymous = factory.CreateClient(); var phone = await FamilyPhone(f); var search = $"{StaffFamilyQuotes}/families?search={Uri.EscapeDataString(phone)}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(search)).StatusCode); foreach (var client in new[] { coach, f.Client }) Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(search)).StatusCode);
        Assert.Empty((await foreign.GetFromJsonAsync<JsonElement>(search)).GetProperty("items").EnumerateArray()); Assert.Equal(HttpStatusCode.NotFound, (await foreign.GetAsync($"{StaffFamilyQuotes}/{f.Guardian}/options")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Post(owner, $"{StaffFamilyQuotes}/{f.Guardian}/quote", new FamilyRenewalWrite(otherFamily.Choices), await Csrf(owner))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"{StaffFamilyQuotes}/{f.Guardian}/quote", new FamilyRenewalWrite(f.Choices))).StatusCode);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Post(owner, $"{StaffFamilyQuotes}/{f.Guardian}/quote", new FamilyRenewalWrite([f.Choices[0], f.Choices[0]]), await Csrf(owner))).StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<JsonElement>($"{StaffFamilyQuotes}/families?search=01")).GetProperty("items").EnumerateArray()); Assert.Equal(HttpStatusCode.UnprocessableEntity, (await owner.GetAsync($"{StaffFamilyQuotes}/families?search={new string('x',101)}")).StatusCode);
    }
    [Fact]
    public async Task Staff_family_quote_requires_all_read_grants_and_never_expands_administrators_branch_scope()
    {
        await using var f = await NewFamily(true); using var owner = await InboxStaff(DemoSeed.OwnerEmail); var email = $"family.quote.{Guid.NewGuid():N}@example.test";
        var saved = await Post(owner, "/api/v1/manage/staff", new StaffWrite("إداري معاينة أسرة اصطناعي", email, "010" + Random.Shared.Next(10000000, 99999999), "Demo-Only-123!", AcademyRole.AcademyAdmin, "اختبار"), await Csrf(owner)); saved.EnsureSuccessStatusCode(); var member = (await saved.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var grants = StaffAccessRules.Modules.Select(m => new StaffActionValue(m, m is "players" or "subscriptions" or "payments", false, false, false)).ToArray();
        async Task Grant() { using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/v1/manage/staff/{member}/access") { Content = JsonContent.Create(new StaffAccessWrite(false, [Slice2DemoSeed.CityBranchId], grants)) }; request.Headers.Add("X-CSRF-TOKEN", await Csrf(owner)); (await owner.SendAsync(request)).EnsureSuccessStatusCode(); }
        await Grant(); using var admin = await InboxStaff(email); var path = $"{StaffFamilyQuotes}/{f.Guardian}"; Assert.Single((await admin.GetFromJsonAsync<JsonElement>(path + "/options")).GetProperty("items").EnumerateArray());
        Assert.Equal(HttpStatusCode.NotFound, (await Post(admin, path + "/quote", new FamilyRenewalWrite(f.Choices), await Csrf(admin))).StatusCode);
        var allowed = await Post(admin, path + "/quote", new FamilyRenewalWrite([f.Choices[0]]), await Csrf(admin)); allowed.EnsureSuccessStatusCode();
        grants = grants.Select(g => g.Module == "players" ? g with { Read = false } : g).ToArray(); await Grant(); Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(path + "/options")).StatusCode);
    }
}
