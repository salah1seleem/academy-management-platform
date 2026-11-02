using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Academy.Api.Auth;
using Academy.Infrastructure.Tenancy;

namespace Academy.IntegrationTests;
public sealed partial class BrandingMatchesTests
{
    [Fact]
    public async Task Self_portraits_persist_for_each_role_but_not_another_actor_or_academy()
    {
        using var owner = await Staff(); using var admin = await Staff(DemoSeed.AdminEmail);
        using var coach = await Mobile(AcademyRole.Coach); using var guardian = await Mobile(AcademyRole.Guardian);
        using var foreign = await Staff(DemoSeed.FutureOwnerEmail);
        foreach (var client in new[] { owner, admin, coach, guardian })
        {
            var csrf = await client.GetFromJsonAsync<JsonElement>("/api/v1/auth/csrf");
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent("SelfPhoto"), "purpose");
            var file = new ByteArrayContent(MediaPng); file.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(file,"file","synthetic-portrait.png");
            using var request = new HttpRequestMessage(HttpMethod.Post,"/api/v1/me/profile/photo") { Content = form };
            request.Headers.Add("X-CSRF-TOKEN",csrf.GetProperty("token").GetString());
            using var upload = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
            var asset = await upload.Content.ReadFromJsonAsync<JsonElement>(); var path = asset.GetProperty("url").GetString()!;
            var profile = await client.GetFromJsonAsync<JsonElement>("/api/v1/me/profile");
            Assert.Equal(path,profile.GetProperty("photoReference").GetString());
            Assert.False(string.IsNullOrWhiteSpace(profile.GetProperty("displayName").GetString()));
            Assert.Equal(HttpStatusCode.OK,(await client.GetAsync(path)).StatusCode);
            foreach (var other in new[] { owner,admin,coach,guardian,foreign }.Where(c=>c!=client))
                Assert.Equal(HttpStatusCode.NotFound,(await other.GetAsync(path)).StatusCode);
        }
        (await Send(owner,"/api/v1/me/profile",new { gender="Female" },true)).EnsureSuccessStatusCode();
        Assert.Equal("Female",(await owner.GetFromJsonAsync<JsonElement>("/api/v1/me/profile")).GetProperty("gender").GetString());
        Assert.Equal(HttpStatusCode.UnprocessableEntity,(await Send(owner,"/api/v1/me/profile",new { gender="infer-from-name" },true)).StatusCode);
    }
}
