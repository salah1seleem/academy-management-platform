using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Demo;
using Academy.Infrastructure.Content;
using Academy.Infrastructure.People;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

public sealed partial class FootballDemoTests
{
    [Fact]
    public async Task Guardian_recommendations_recognize_imported_football_kind_without_English_name()
    {
        using var guardian = await MobileLogin(FootballDemoSeed.Phone(100));
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var sport = await db.Sports.SingleAsync(s => s.AcademyId == AcademyId && s.Id == FootballDemoSeed.Id("sport"));
        var previousName = sport.EnglishName;
        var previousKind = sport.Kind;
        var before = await db.Collections.CountAsync(c => c.AcademyId == AcademyId);
        try
        {
            sport.EnglishName = null;
            sport.Kind = Academy.Infrastructure.Structure.SportKind.Football;
            await db.SaveChangesAsync();
            var response = await guardian.GetAsync($"/api/v1/guardian/children/{FootballDemoSeed.Id("player/0")}/nutrition/recommendations?date=2026-09-28");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var source = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.NotEqual(JsonValueKind.Null, source.GetProperty("breakfast").ValueKind);
            Assert.Equal(before, await db.Collections.CountAsync(c => c.AcademyId == AcademyId));
        }
        finally
        {
            sport.EnglishName = previousName;
            sport.Kind = previousKind;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Guardian_meal_rail_sources_hide_other_academy_player_and_meal_without_financial_effect()
    {
        var otherAcademy = Guid.Parse("90000000-0058-4000-8000-000000000001");
        var otherPlayer = Guid.Parse("90000000-0058-4000-8000-000000000002");
        var otherMeal = Guid.Parse("90000000-0058-4000-8000-000000000003");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        db.Academies.Add(new Academy.Infrastructure.Tenancy.Academy { Id = otherAcademy, Slug = "test-meal-rail-isolation-58", ArabicName = "أكاديمية عزل وجبات اصطناعية", DefaultCurrency = "EGP", TimeZone = "Africa/Cairo" });
        db.Players.Add(new Player { Id = otherPlayer, AcademyId = otherAcademy, PlayerCode = "MEAL-58", ArabicName = "طفل عزل اصطناعي", DateOfBirth = new DateOnly(2018, 3, 12) });
        db.NutritionItems.Add(new NutritionItem { Id = otherMeal, AcademyId = otherAcademy, ArabicName = "وجبة عزل اصطناعية", ArabicDescription = "وصف اختباري", ImageReference = "/demo-assets/nutrition/breakfast-1.jpg", ServingDescription = "حصة اختبارية" });
        await db.SaveChangesAsync();
        try
        {
            using var guardian = await MobileLogin(FootballDemoSeed.Phone(100));
            var beforeCount = await db.Collections.CountAsync(x => x.AcademyId == AcademyId);
            var beforeAmount = await db.Collections.Where(x => x.AcademyId == AcademyId).SumAsync(x => x.Amount);
            Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync($"/api/v1/guardian/children/{otherPlayer}/nutrition/recommendations?date=2026-09-28")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await guardian.GetAsync($"/api/v1/guardian/nutrition/{otherMeal}")).StatusCode);
            var list = await guardian.GetStringAsync("/api/v1/guardian/nutrition?category=Breakfast");
            Assert.DoesNotContain(otherMeal.ToString(), list);
            Assert.DoesNotContain("وجبة عزل اصطناعية", list);
            var source = await guardian.GetFromJsonAsync<JsonElement>($"/api/v1/guardian/children/{FootballDemoSeed.Id("player/0")}/nutrition/recommendations?date=2026-09-28");
            var mealId = source.GetProperty("breakfast").GetProperty("id").GetGuid();
            Assert.Equal(HttpStatusCode.OK, (await guardian.GetAsync($"/api/v1/guardian/nutrition/{mealId}")).StatusCode);
            Assert.Equal(beforeCount, await db.Collections.CountAsync(x => x.AcademyId == AcademyId));
            Assert.Equal(beforeAmount, await db.Collections.Where(x => x.AcademyId == AcademyId).SumAsync(x => x.Amount));
        }
        finally
        {
            await db.NutritionItems.IgnoreQueryFilters().Where(x => x.Id == otherMeal && x.AcademyId == otherAcademy).ExecuteDeleteAsync();
            await db.Players.IgnoreQueryFilters().Where(x => x.Id == otherPlayer && x.AcademyId == otherAcademy).ExecuteDeleteAsync();
            await db.Academies.Where(x => x.Id == otherAcademy).ExecuteDeleteAsync();
        }
    }
}
