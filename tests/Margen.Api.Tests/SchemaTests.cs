using Margen.Api.Tests.Infra;
using Npgsql;

namespace Margen.Api.Tests;

/// <summary>
/// Lo que se comprueba aquí no es el modelo de EF Core: es el esquema que
/// quedó en Postgres después de aplicar la migración. Es la diferencia entre
/// creer que una columna es <c>bigint</c> y saberlo.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class SchemaTests(PostgresFixture postgres)
{
    private static readonly string[] TablasEsperadas =
    [
        "Accounts",
        "Transactions",
        "Categories",
        "BudgetPeriods",
        "CategoryBudgets",
        "MerchantRules",
        "RecurringPayments",
        "IncomingEmails",
        "Alerts",
        "Devices",
        "DeviceChallenges",
        "AccessTokens",
    ];

    [Fact]
    public async Task la_migracion_inicial_crea_el_esquema_completo()
    {
        List<string> tablas = await QueryStringsAsync(
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public' AND table_type = 'BASE TABLE'
            """);

        foreach (string esperada in TablasEsperadas)
        {
            Assert.Contains(esperada, tablas);
        }
    }

    [Fact]
    public async Task ninguna_columna_es_de_punto_flotante()
    {
        // No se limita a las columnas que hoy parecen monetarias. Cualquier
        // columna de punto flotante en esta base es un sitio por donde el
        // dinero puede terminar entrando mañana sin que nadie lo revise.
        List<string> columnas = await QueryStringsAsync(
            """
            SELECT table_name || '.' || column_name || ' (' || data_type || ')'
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND data_type IN ('double precision', 'real', 'numeric', 'money')
            """);

        Assert.Empty(columnas);
    }

    [Fact]
    public async Task toda_columna_monetaria_es_bigint()
    {
        List<string> columnas = await QueryStringsAsync(
            """
            SELECT table_name || '.' || column_name || ' -> ' || data_type
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND (column_name IN (
                     'Amount', 'Balance', 'CreditLimit', 'Allocated', 'Adjustment',
                     'ExpectedIncome', 'ActualIncome', 'SafetyFund',
                     'CommittedSavings', 'ExpectedAmount'))
              AND data_type <> 'bigint'
            """);

        Assert.Empty(columnas);
    }

    [Fact]
    public async Task ninguna_columna_de_instante_pierde_la_zona()
    {
        List<string> columnas = await QueryStringsAsync(
            """
            SELECT table_name || '.' || column_name || ' -> ' || data_type
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND data_type LIKE 'timestamp%'
              AND data_type <> 'timestamp with time zone'
            """);

        Assert.Empty(columnas);
    }

    [Fact]
    public async Task hay_columnas_de_instante_de_verdad()
    {
        // Sin esta comprobación, la prueba de arriba pasaría en verde con un
        // esquema que no tuviera ni una sola columna de fecha.
        List<string> columnas = await QueryStringsAsync(
            """
            SELECT table_name || '.' || column_name
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND data_type = 'timestamp with time zone'
            """);

        Assert.NotEmpty(columnas);
    }

    [Fact]
    public async Task el_correo_entrante_nace_con_sus_dos_defensas_contra_el_duplicado()
    {
        // Índice único sobre el identificador de mensaje y sobre la huella del
        // cuerpo. La Fase 6 los usa; nacen en la migración inicial porque
        // añadir un índice único a una tabla que ya acumuló duplicados es una
        // migración que falla en el servidor.
        List<string> indices = await QueryStringsAsync(
            """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'public' AND tablename = 'IncomingEmails'
            """);

        Assert.Contains(indices, i => i.Contains("UNIQUE", StringComparison.Ordinal)
            && i.Contains("MessageId", StringComparison.Ordinal));

        Assert.Contains(indices, i => i.Contains("UNIQUE", StringComparison.Ordinal)
            && i.Contains("BodyHash", StringComparison.Ordinal));
    }

    [Fact]
    public async Task el_movimiento_tiene_huella_unica()
    {
        List<string> indices = await QueryStringsAsync(
            """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'public' AND tablename = 'Transactions'
            """);

        Assert.Contains(indices, i => i.Contains("UNIQUE", StringComparison.Ordinal)
            && i.Contains("Fingerprint", StringComparison.Ordinal));
    }

    [Fact]
    public async Task el_token_se_guarda_hasheado_y_es_unico()
    {
        List<string> indices = await QueryStringsAsync(
            """
            SELECT indexdef
            FROM pg_indexes
            WHERE schemaname = 'public' AND tablename = 'AccessTokens'
            """);

        Assert.Contains(indices, i => i.Contains("UNIQUE", StringComparison.Ordinal)
            && i.Contains("TokenHash", StringComparison.Ordinal));
    }

    private async Task<List<string>> QueryStringsAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand(sql, connection);
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();

        var results = new List<string>();
        while (await reader.ReadAsync())
        {
            results.Add(reader.GetString(0));
        }

        return results;
    }
}
