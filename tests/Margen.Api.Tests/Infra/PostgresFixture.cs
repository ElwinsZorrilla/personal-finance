using System.Security.Cryptography;
using Margen.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Margen.Api.Tests.Infra;

/// <summary>
/// Un PostgreSQL 16 real en contenedor, creado en limpio para la tanda de
/// pruebas.
/// </summary>
/// <remarks>
/// No es una base en memoria a propósito. Lo que estas pruebas comprueban —que
/// una columna sea <c>timestamptz</c> y no <c>timestamp</c>, que ninguna sea de
/// punto flotante, que un índice único impida el duplicado— son cosas que un
/// proveedor en memoria no tiene. Una prueba en memoria diría que todo está
/// bien y no habría mirado nada de eso.
/// </remarks>
public sealed class PostgresFixture : IAsyncLifetime
{
    /// <summary>
    /// La misma versión mayor que el compose de producción. Probar contra 17
    /// lo que se despliega sobre 16 comprueba otro motor.
    /// </summary>
    private const string Image = "postgres:16-alpine";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image)
        .WithDatabase("margen_test")
        .WithUsername("margen")

        // Se genera en cada ejecución. Una contraseña escrita en el archivo,
        // aunque sea de un contenedor que vive treinta segundos, es una
        // contraseña en el repositorio.
        .WithPassword(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16)))
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        // El contenedor por defecto de Testcontainers no fija zona horaria. Se
        // deja así adrede: si las pruebas de zona pasan contra un servidor en
        // UTC, es porque el instante viaja bien y no porque el servidor esté
        // configurado para disimularlo.
        ConnectionString = _container.GetConnectionString();

        // La migración se aplica una vez, aquí, y no desde el servicio de
        // arranque del API: si cada aplicación de prueba migrara por su cuenta,
        // dos que arrancan a la vez competirían por el mismo esquema.
        var options = new DbContextOptionsBuilder<MargenDbContext>()
            .UseNpgsql(ConnectionString)
            .Options;

        await using var db = new MargenDbContext(options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}

/// <summary>
/// Agrupa las pruebas que comparten el contenedor. Levantar un Postgres por
/// clase de prueba multiplicaría por seis el tiempo de la tanda.
/// </summary>
[CollectionDefinition(PostgresCollection.Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
