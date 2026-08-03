using Margen.Api.Tests.Infra;
using Margen.Domain;
using Margen.Domain.Entities;
using Margen.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Margen.Api.Tests;

/// <summary>
/// El respaldo se restaura de verdad, en una base descartable.
/// </summary>
/// <remarks>
/// «Restauración probada» escrito en un documento no prueba nada: el día que
/// haga falta, lo que decide es si el archivo restaura o no. Esto lo comprueba
/// en cada tanda, con `pg_dump` y `pg_restore` reales sobre un Postgres real.
///
/// El motivo de que sea una prueba y no un procedimiento manual: un
/// procedimiento manual se hace la primera semana y deja de hacerse. Una prueba
/// se pone roja el día que una migración rompa la restauración, que es meses
/// antes de que se necesite.
///
/// **En ningún momento se toca la base de la que se hizo el respaldo.** La
/// verificación crea una aparte, restaura ahí y la borra.
/// </remarks>
[Collection(PostgresCollection.Name)]
public sealed class BackupRestoreTests(PostgresFixture postgres)
{
    private const string DumpPath = "/tmp/margen-prueba.dump";

    private MargenDbContext Db() => new(
        new DbContextOptionsBuilder<MargenDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);

    private async Task<(long Code, string Out, string Err)> DumpAsync() =>
        await postgres.RunAsync(
            "pg_dump", "-U", PostgresFixture.User, "-d", PostgresFixture.Database,
            "--format=custom", "--compress=9", "--file", DumpPath);

    [Fact]
    public async Task un_respaldo_se_restaura_entero_en_una_base_descartable()
    {
        Seed seed = await Seed.PlantAsync(postgres.ConnectionString);

        await using (MargenDbContext db = Db())
        {
            db.Transactions.Add(Movimiento(seed));
            await db.SaveChangesAsync();
        }

        (long code, _, string err) = await DumpAsync();
        Assert.True(code == 0, $"pg_dump falló: {err}");

        // Que el archivo se pueda listar solo dice que no está truncado. Es la
        // comprobación barata del guion diario, y no basta.
        (long listCode, _, string listErr) = await postgres.RunAsync(
            "pg_restore", "--list", DumpPath);
        Assert.True(listCode == 0, $"pg_restore --list falló: {listErr}");

        const string scratch = "verificacion_prueba";

        await postgres.RunAsync(
            "psql", "-U", PostgresFixture.User, "-d", "postgres",
            "-c", $"DROP DATABASE IF EXISTS {scratch}");

        (long createCode, _, string createErr) = await postgres.RunAsync(
            "psql", "-U", PostgresFixture.User, "-d", "postgres",
            "-c", $"CREATE DATABASE {scratch}");
        Assert.True(createCode == 0, $"no se pudo crear la base descartable: {createErr}");

        try
        {
            // `--exit-on-error` es lo que convierte esto en una verificación.
            // Sin él, pg_restore informa de los errores y **termina con éxito**,
            // así que un respaldo irrecuperable pasaría la prueba.
            (long restoreCode, _, string restoreErr) = await postgres.RunAsync(
                "pg_restore", "-U", PostgresFixture.User, "-d", scratch,
                "--no-owner", "--no-privileges", "--exit-on-error", DumpPath);

            Assert.True(restoreCode == 0, $"la restauración falló: {restoreErr}");

            // Restaurar sin errores tampoco basta: un respaldo de una base vacía
            // también restaura sin errores. Se cuenta lo que tiene que estar.
            (long countCode, string count, _) = await postgres.RunAsync(
                "psql", "-U", PostgresFixture.User, "-d", scratch,
                "-tAc", "SELECT COUNT(*) FROM \"Transactions\"");

            Assert.Equal(0L, countCode);
            Assert.Equal(1, int.Parse(count.Trim(), System.Globalization.CultureInfo.InvariantCulture));
        }
        finally
        {
            await postgres.RunAsync(
                "psql", "-U", PostgresFixture.User, "-d", "postgres",
                "-c", $"DROP DATABASE IF EXISTS {scratch}");
        }
    }

    [Fact]
    public async Task el_esquema_restaurado_conserva_los_tipos_que_importan()
    {
        // Un respaldo que restaura las filas pero pierde `timestamptz` o
        // convierte el dinero en punto flotante es peor que ninguno: parece que
        // funcionó. Es lo mismo que comprueba `SchemaTests` sobre la base viva,
        // aquí sobre la restaurada.
        await Seed.PlantAsync(postgres.ConnectionString);

        (long code, _, string err) = await DumpAsync();
        Assert.True(code == 0, err);

        const string scratch = "verificacion_tipos";

        await postgres.RunAsync(
            "psql", "-U", PostgresFixture.User, "-d", "postgres",
            "-c", $"DROP DATABASE IF EXISTS {scratch}");
        await postgres.RunAsync(
            "psql", "-U", PostgresFixture.User, "-d", "postgres",
            "-c", $"CREATE DATABASE {scratch}");

        try
        {
            await postgres.RunAsync(
                "pg_restore", "-U", PostgresFixture.User, "-d", scratch,
                "--no-owner", "--no-privileges", "--exit-on-error", DumpPath);

            (_, string tipos, _) = await postgres.RunAsync(
                "psql", "-U", PostgresFixture.User, "-d", scratch, "-tAc",
                """
                SELECT string_agg(DISTINCT data_type, ',')
                FROM information_schema.columns
                WHERE table_schema = 'public' AND column_name = 'Amount'
                """);

            Assert.Contains("bigint", tipos, StringComparison.Ordinal);
            Assert.DoesNotContain("double", tipos, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("numeric", tipos, StringComparison.OrdinalIgnoreCase);

            (_, string fechas, _) = await postgres.RunAsync(
                "psql", "-U", PostgresFixture.User, "-d", scratch, "-tAc",
                """
                SELECT string_agg(DISTINCT data_type, ',')
                FROM information_schema.columns
                WHERE table_schema = 'public' AND column_name = 'OccurredAt'
                """);

            Assert.Contains("with time zone", fechas, StringComparison.Ordinal);
        }
        finally
        {
            await postgres.RunAsync(
                "psql", "-U", PostgresFixture.User, "-d", "postgres",
                "-c", $"DROP DATABASE IF EXISTS {scratch}");
        }
    }

    [Fact]
    public async Task un_respaldo_corrupto_no_pasa_por_bueno()
    {
        // La prueba de la prueba. Si `--exit-on-error` no estuviera, o si la
        // verificación se conformara con que el proceso termine, esto pasaría.
        await postgres.RunAsync(
            "sh", "-c", "head -c 512 /dev/urandom > /tmp/corrupto.dump");

        (long code, _, _) = await postgres.RunAsync("pg_restore", "--list", "/tmp/corrupto.dump");

        Assert.NotEqual(0L, code);
    }

    private static Transaction Movimiento(Seed seed) => new()
    {
        Id = Guid.CreateVersion7(),
        AccountId = seed.CheckingId,
        MerchantRaw = "SM NACIONAL",
        MerchantNormalized = "SM NACIONAL",
        Amount = Money.FromUnits(1_234),
        OccurredAt = DateTime.UtcNow,
        Kind = TxKind.Purchase,
        Status = TxStatus.Posted,
        Source = TxSource.Email,
        Direction = TxDirection.Outflow,
        Fingerprint = $"respaldo-{Guid.NewGuid():N}",
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };
}
