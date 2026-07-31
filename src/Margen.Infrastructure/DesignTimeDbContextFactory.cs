using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Margen.Infrastructure;

/// <summary>
/// Construye el contexto para <c>dotnet ef migrations</c>.
/// </summary>
/// <remarks>
/// Generar una migración no abre ninguna conexión: EF Core solo necesita saber
/// qué proveedor traduce el modelo. Por eso la cadena de aquí es un marcador de
/// posición sin credenciales y no la de ningún servidor real.
///
/// Existe para que las herramientas no necesiten al proyecto del API como
/// proyecto de arranque, que obligaría a añadirle el paquete de diseño de EF
/// Core y a que la imagen de producción lo llevara dentro.
/// </remarks>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MargenDbContext>
{
    private const string PlaceholderConnection =
        "Host=localhost;Port=5432;Database=margen_design;Username=margen";

    public MargenDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<MargenDbContext>()
            .UseNpgsql(PlaceholderConnection)
            .Options;

        return new MargenDbContext(options);
    }
}
