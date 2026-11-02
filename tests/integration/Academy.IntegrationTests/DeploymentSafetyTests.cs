using System.Net;
using System.Net.Http.Json;
using Academy.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

[Collection("Tenant authentication database")]
public sealed class DeploymentSafetyTests
{
    [Fact]
    public async Task Production_denies_demo_OTP_and_retains_protected_session_data_between_hosts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "iacademy-keys-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string protectedValue;
            await using (var first = new Factory(directory))
            {
                using var client = first.CreateClient();
                var response = await client.PostAsJsonAsync("/api/v1/mobile/auth/otp/request", new { phoneNumber = "+201000000001" });
                Assert.Equal(HttpStatusCode.NotImplemented, response.StatusCode);
                var protector = first.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("deployment-session-test");
                protectedValue = protector.Protect("fictional-session");
                using var scope = first.Services.CreateScope();
                Assert.Empty(await scope.ServiceProvider.GetRequiredService<FoundationDbContext>().Database.GetPendingMigrationsAsync());
            }
            await using var second = new Factory(directory);
            _ = second.CreateClient();
            var restored = second.Services.GetRequiredService<IDataProtectionProvider>().CreateProtector("deployment-session-test").Unprotect(protectedValue);
            Assert.Equal("fictional-session", restored);
            Assert.NotEmpty(Directory.GetFiles(directory, "key-*.xml"));
        }
        finally { Directory.Delete(directory, true); }
    }

    private sealed class Factory(string keys) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable("ACADEMY_TEST_CONNECTION_STRING") ?? throw new InvalidOperationException("Isolated database required"));
            builder.UseSetting("DataProtection:KeyPath", keys);
            builder.UseSetting("Demo:SeedEnabled", "false");
            builder.UseSetting("Demo:FixedOtpEnabled", "false");
            builder.UseSetting("Payments:InternalTest:Enabled", "false");
            builder.UseSetting("Imports:WorkerEnabled", "false");
            builder.UseSetting("Notifications:WorkerEnabled", "false");
        }
    }
}
