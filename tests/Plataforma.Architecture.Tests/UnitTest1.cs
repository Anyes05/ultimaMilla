using NetArchTest.Rules;
using Xunit;

namespace Plataforma.Architecture.Tests;

public class DomainArchitectureTests
{
    [Fact]
    public void Domain_NoDeberiaDependerDe_OtrasCapas()
    {
        // Arrange
        var assembly = typeof(Plataforma.Domain.EstadoEnvio).Assembly;

        var otrasCapas = new[]
        {
            "Plataforma.Application",
            "Plataforma.Infrastructure",
            "Plataforma.Backoffice",
            "Plataforma.PortalComercio"
        };

        // Act
        var result = Types.InAssembly(assembly)
            .ShouldNot()
            .HaveDependencyOnAny(otrasCapas)
            .GetResult();

        // Assert
        Assert.True(result.IsSuccessful, "La capa Domain no debe tener dependencias hacia capas externas.");
    }
}
