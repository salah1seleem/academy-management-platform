using Academy.Infrastructure;

namespace Academy.ArchitectureTests;

public sealed class DependencyRulesTests
{
    [Fact]
    public void Infrastructure_does_not_reference_the_api_host()
    {
        var references = typeof(InfrastructureAssemblyMarker)
            .Assembly
            .GetReferencedAssemblies()
            .Select(assembly => assembly.Name);

        Assert.DoesNotContain("Academy.Api", references);
    }
}
