using Margen.Domain;
using Margen.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Margen.Api.Tests;

/// <summary>
/// Comprobaciones sobre el modelo de EF Core en lugar de sobre el esquema ya
/// creado.
/// </summary>
/// <remarks>
/// <see cref="SchemaTests"/> interroga a <c>information_schema</c> con una
/// lista de nombres de columna escrita a mano, y esa lista envejece: una
/// columna monetaria nueva con un nombre nuevo no aparecería en ella. Estas
/// pruebas recorren el modelo y encuentran cualquier propiedad de tipo
/// <see cref="Money"/> o <see cref="DateTime"/>, se llame como se llame.
/// </remarks>
public sealed class ModelTests
{
    [Fact]
    public void toda_propiedad_de_dinero_va_a_una_columna_bigint()
    {
        var mal = new List<string>();

        foreach (IProperty property in MoneyProperties())
        {
            string? columnType = property.GetColumnType();
            if (columnType != "bigint")
            {
                mal.Add($"{property.DeclaringType.ShortName()}.{property.Name} -> {columnType}");
            }
        }

        Assert.Empty(mal);
    }

    [Fact]
    public void hay_propiedades_de_dinero_de_verdad()
    {
        // Sin esto, la prueba de arriba pasaría con un modelo que no tuviera ni
        // una sola columna monetaria.
        Assert.NotEmpty(MoneyProperties());
    }

    [Fact]
    public void toda_propiedad_de_instante_va_a_una_columna_con_zona()
    {
        var mal = new List<string>();

        foreach (IEntityType entity in Model().GetEntityTypes())
        {
            foreach (IProperty property in entity.GetProperties())
            {
                Type type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (type != typeof(DateTime))
                {
                    continue;
                }

                string? columnType = property.GetColumnType();
                if (columnType != "timestamp with time zone")
                {
                    mal.Add($"{entity.ShortName()}.{property.Name} -> {columnType}");
                }
            }
        }

        Assert.Empty(mal);
    }

    [Fact]
    public void ninguna_propiedad_del_modelo_es_de_punto_flotante()
    {
        var mal = new List<string>();

        foreach (IEntityType entity in Model().GetEntityTypes())
        {
            foreach (IProperty property in entity.GetProperties())
            {
                Type type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (type == typeof(double) || type == typeof(float) || type == typeof(decimal))
                {
                    mal.Add($"{entity.ShortName()}.{property.Name} ({type.Name})");
                }
            }
        }

        Assert.Empty(mal);
    }

    private static List<IProperty> MoneyProperties() =>
    [
        .. Model().GetEntityTypes()
            .SelectMany(e => e.GetProperties())
            .Where(p => (Nullable.GetUnderlyingType(p.ClrType) ?? p.ClrType) == typeof(Money)),
    ];

    /// <summary>
    /// El modelo se construye sin abrir conexión: EF Core solo necesita saber
    /// qué proveedor traduce, no hablar con él.
    /// </summary>
    private static IModel Model()
    {
        var options = new DbContextOptionsBuilder<MargenDbContext>()
            .UseNpgsql("Host=localhost;Database=margen_modelo;Username=margen")
            .Options;

        using var db = new MargenDbContext(options);
        return db.Model;
    }
}
