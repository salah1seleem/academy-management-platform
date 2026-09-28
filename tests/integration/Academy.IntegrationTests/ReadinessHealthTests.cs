using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Academy.IntegrationTests;

public sealed class ReadinessHealthTests
{
    [Fact]
    public async Task Ready_is_healthy_when_postgresql_is_available()
    {
        var connectionString = Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING");
        Assert.False(
            string.IsNullOrWhiteSpace(connectionString),
            "ACADEMY_TEST_CONNECTION_STRING is required for the PostgreSQL integration test.");

        await using var factory = new TestApiFactory(connectionString!);
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ready_is_unavailable_when_postgresql_is_unavailable()
    {
        await using var factory = new TestApiFactory(
            "Host=127.0.0.1;Port=1;Database=unavailable;Username=unused;Password=unused;Timeout=1");
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private sealed class TestApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ConnectionStrings:Default", connectionString);
        }
    }
}
