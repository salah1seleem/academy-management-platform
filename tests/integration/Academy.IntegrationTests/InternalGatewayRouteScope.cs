using Academy.Api.Auth;
using Academy.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Academy.IntegrationTests;

// InternalTest gateway scenarios must not depend on another suite enabling manual transfers.
// The serialized collection and finally/disposal restore all pre-existing synthetic settings.
internal sealed class InternalGatewayRouteScope(IServiceProvider services, Guid[] suspended) : IAsyncDisposable
{
    public static async Task<InternalGatewayRouteScope> Begin(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        var ids = await db.AcademyTransferSettings.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && x.IsActive).Select(x => x.Id).ToArrayAsync();
        await db.AcademyTransferSettings.Where(x => ids.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));
        return new(services, ids);
    }
    public async ValueTask DisposeAsync()
    {
        await using var scope = services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<FoundationDbContext>();
        await db.AcademyTransferSettings.Where(x => x.AcademyId == DemoSeed.NogoomAcademyId && suspended.Contains(x.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, true));
    }
}
