using Academy.Infrastructure.Structure;
namespace Academy.Infrastructure.Operations;
public sealed class PageHelpVideo : TenantEntity
{
    public required string Role {get;set;}
    public required string Screen {get;set;}
    public required string Title {get;set;}
    public required string Url {get;set;}
}
