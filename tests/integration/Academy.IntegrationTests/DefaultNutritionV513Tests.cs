using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Demo;
using Academy.Api.Slice7;
using Academy.Infrastructure.Persistence;
using Academy.Infrastructure.Tenancy;
using Academy.Infrastructure.Structure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace Academy.IntegrationTests;
public sealed partial class FootballDemoTests
{
 [Fact] public async Task Default_library_is_complete_repeatable_and_isolated_without_money_or_medical_rows(){
  await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
  var a=Guid.NewGuid();var b=Guid.NewGuid();
  db.Academies.AddRange(new Academy.Infrastructure.Tenancy.Academy{Id=a,Slug="nutrition-v513-"+a.ToString("N"),ArabicName="أكاديمية غذاء خيالية",DefaultCurrency="EGP",TimeZone="Africa/Cairo"},new Academy.Infrastructure.Tenancy.Academy{Id=b,Slug="nutrition-v513-"+b.ToString("N"),ArabicName="أكاديمية عزل خيالية",DefaultCurrency="EGP",TimeZone="Africa/Cairo"});await db.SaveChangesAsync();
  var featureService=scope.ServiceProvider.GetRequiredService<Academy.Api.Configuration.AcademyConfigurationService>();
  Assert.True(await featureService.Enabled(a,"nutrition")); Assert.False(await featureService.Enabled(a,"catalog")); Assert.False(await featureService.Enabled(Guid.NewGuid(),"nutrition"));
  var finance=await db.Collections.CountAsync();var medical=await db.PlayerMedicalRecords.CountAsync();
  await DefaultNutritionLibrary.EnsureAsync(db,a);await DefaultNutritionLibrary.EnsureAsync(db,a);
  Assert.Equal(24,await db.NutritionItems.CountAsync(i=>i.AcademyId==a));Assert.Equal(3,await db.NutritionCategoryLinks.Where(i=>i.AcademyId==a).Select(i=>i.Category).Distinct().CountAsync());
  Assert.Equal(0,await db.NutritionItems.CountAsync(i=>i.AcademyId==b));
  await DefaultNutritionLibrary.EnsureAsync(db,b);Assert.Equal(24,await db.NutritionItems.CountAsync(i=>i.AcademyId==b));
  Assert.Equal(finance,await db.Collections.CountAsync());Assert.Equal(medical,await db.PlayerMedicalRecords.CountAsync());
 }
 [Fact] public async Task Swimming_child_gets_own_daily_nutrition_and_foreign_child_stays_hidden(){
  using var guardian=await MobileLogin(FootballDemoSeed.Phone(100));
  await using var scope=factory.Services.CreateAsyncScope();var db=scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
  var sport=await db.Sports.SingleAsync(s=>s.AcademyId==AcademyId&&s.Id==FootballDemoSeed.Id("sport"));var kind=sport.Kind;
  try{sport.Kind=SportKind.Swimming;await db.SaveChangesAsync();
    var r=await guardian.GetAsync($"/api/v1/guardian/children/{FootballDemoSeed.Id("player/0")}/nutrition/recommendations?date=2026-10-28");Assert.Equal(HttpStatusCode.OK,r.StatusCode);
    var body=await r.Content.ReadFromJsonAsync<JsonElement>();Assert.NotEqual(JsonValueKind.Null,body.GetProperty("breakfast").ValueKind);
    Assert.Equal(HttpStatusCode.NotFound,(await guardian.GetAsync($"/api/v1/guardian/children/{Guid.NewGuid()}/nutrition/recommendations")).StatusCode);
  }finally{sport.Kind=kind;await db.SaveChangesAsync();}
 }
}
