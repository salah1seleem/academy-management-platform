using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Academy.IntegrationTests;
internal static class PrivateMediaTestClient
{
    // TestServer's default RedirectHandler strips bearer headers. Exercise the same
    // explicit, bounded same-origin redirect contract as the supported native client.
    // Cookie browser clients keep their normal cookie/redirect handling.
    internal static async Task<HttpResponseMessage> GetPrivateAssetAsync(this HttpClient source,WebApplicationFactory<Program> factory,string path)
    {
        if(source.DefaultRequestHeaders.Authorization==null)return await source.GetAsync(path);
        using var client=new HttpClient(factory.Server.CreateHandler()){BaseAddress=source.BaseAddress};
        client.DefaultRequestHeaders.Authorization=source.DefaultRequestHeaders.Authorization;
        var first=await client.GetAsync(path);
        if(first.StatusCode!=HttpStatusCode.Redirect)return first;
        var original=new Uri(source.BaseAddress!,path);var location=first.Headers.Location;
        var next=location==null?null:new Uri(original,location);
        var query=next==null?null:Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(next.Query);
        Assert.Matches("^/api/v1/assets/[a-fA-F0-9-]{36}$",original.AbsolutePath);
        Assert.NotNull(next);Assert.Equal(original.Scheme,next.Scheme);Assert.Equal(original.Authority,next.Authority);Assert.Equal(original.AbsolutePath,next.AbsolutePath);Assert.Empty(next.UserInfo);Assert.Empty(next.Fragment);Assert.NotNull(query);Assert.Single(query);Assert.Single(query["access"]);Assert.Matches("^ML1\\.[A-Za-z0-9_-]+$",query["access"][0]!);
        first.Dispose();return await client.GetAsync(next);
    }
}
