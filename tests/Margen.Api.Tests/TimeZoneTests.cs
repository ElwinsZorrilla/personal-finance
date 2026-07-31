using Margen.Api.Tests.Infra;
using Margen.Domain;
using Margen.Domain.Entities;
using Margen.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Margen.Api.Tests;

/// <summary>
/// El riesgo que cubren estas pruebas es concreto: una compra de las 11:30 de
/// la noche que aparece al día siguiente y cae en otro período presupuestario.
/// El usuario ve un gasto que no hizo ese día y un período que no cuadra.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class TimeZoneTests(PostgresFixture postgres)
{
    private static readonly TimeZoneInfo SantoDomingo =
        TimeZoneInfo.FindSystemTimeZoneById("America/Santo_Domingo");

    [Fact]
    public void la_zona_de_santo_domingo_existe_en_esta_maquina()
    {
        // Sin base de datos de zonas horarias, todas las pruebas de abajo
        // pasarían comparando UTC contra UTC.
        Assert.Equal(TimeSpan.FromHours(-4), SantoDomingo.GetUtcOffset(new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc)));
    }

    [Fact]
    public async Task una_compra_de_las_23_30_no_cambia_de_dia()
    {
        var horaLocal = new DateTime(2026, 3, 15, 23, 30, 0, DateTimeKind.Unspecified);
        DateTime instanteUtc = TimeZoneInfo.ConvertTimeToUtc(horaLocal, SantoDomingo);

        // Santo Domingo está en UTC-4 todo el año: las 23:30 del 15 son las
        // 03:30 UTC del 16. Ese salto de día es justo lo que no puede llegar a
        // la pantalla.
        Assert.Equal(new DateTime(2026, 3, 16, 3, 30, 0, DateTimeKind.Utc), instanteUtc);

        Guid id = await GuardarMovimientoAsync(instanteUtc);

        await using MargenDbContext db = CrearContexto();
        Transaction leido = await db.Transactions.AsNoTracking().SingleAsync(t => t.Id == id);

        Assert.Equal(DateTimeKind.Utc, leido.OccurredAt.Kind);
        Assert.Equal(instanteUtc, leido.OccurredAt);

        DateTime devueltoALocal = TimeZoneInfo.ConvertTimeFromUtc(leido.OccurredAt, SantoDomingo);

        Assert.Equal(horaLocal, devueltoALocal);
        Assert.Equal(15, devueltoALocal.Day);
        Assert.Equal(23, devueltoALocal.Hour);
        Assert.Equal(30, devueltoALocal.Minute);
    }

    [Fact]
    public async Task la_compra_de_las_23_30_cae_en_el_periodo_que_cierra_ese_dia()
    {
        var horaLocal = new DateTime(2026, 3, 15, 23, 30, 0, DateTimeKind.Unspecified);
        DateTime instanteUtc = TimeZoneInfo.ConvertTimeToUtc(horaLocal, SantoDomingo);

        Guid id = await GuardarMovimientoAsync(instanteUtc);

        await using MargenDbContext db = CrearContexto();
        Transaction leido = await db.Transactions.AsNoTracking().SingleAsync(t => t.Id == id);

        // Un período que cierra el 15. Clasificar por la fecha UTC lo dejaría
        // fuera; clasificar por la fecha local lo deja dentro, que es donde el
        // usuario lo hizo.
        var inicio = new DateOnly(2026, 2, 16);
        var fin = new DateOnly(2026, 3, 15);

        DateOnly diaLocal = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTimeFromUtc(leido.OccurredAt, SantoDomingo));

        Assert.InRange(diaLocal, inicio, fin);

        // Y para dejar constancia de por qué importa: por UTC habría caído
        // fuera.
        Assert.False(DateOnly.FromDateTime(leido.OccurredAt) <= fin);
    }

    [Fact]
    public async Task guardar_un_instante_sin_zona_falla_en_lugar_de_inventarla()
    {
        // Npgsql rechaza un DateTime que no es UTC en una columna timestamptz.
        // Se comprueba a propósito: la alternativa —desactivar la validación—
        // haría que una hora local se guardara como si fuera UTC y el
        // movimiento se corriera cuatro horas sin que nadie se entere.
        var sinZona = new DateTime(2026, 3, 15, 23, 30, 0, DateTimeKind.Unspecified);
        Guid id = Guid.CreateVersion7();

        // Se exige el tipo exacto. `ThrowsAny<Exception>` habría pasado en
        // verde con la base caída, sin haber comprobado nada de lo que dice
        // comprobar.
        DbUpdateException fallo = await Assert.ThrowsAsync<DbUpdateException>(
            () => GuardarMovimientoAsync(sinZona, id));

        Assert.IsType<ArgumentException>(fallo.InnerException);

        // Y lo que de verdad importa, sin depender de cómo redacte Npgsql su
        // mensaje: no quedó nada escrito. Un rechazo que hubiera guardado la
        // hora local como si fuera UTC habría corrido el movimiento cuatro
        // horas sin avisar a nadie.
        await using MargenDbContext db = CrearContexto();
        Assert.False(await db.Transactions.AnyAsync(t => t.Id == id));
    }

    private async Task<Guid> GuardarMovimientoAsync(DateTime occurredAt, Guid? id = null)
    {
        await using MargenDbContext db = CrearContexto();

        var cuenta = new Account
        {
            Id = Guid.CreateVersion7(),
            Name = "Cuenta de prueba",
            LastFour = "0001",
            Kind = AccountKind.Checking,
            Balance = Money.FromUnits(10_000),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var movimiento = new Transaction
        {
            Id = id ?? Guid.CreateVersion7(),
            AccountId = cuenta.Id,
            MerchantRaw = "SUPERMERCADO NACIONAL",
            MerchantNormalized = "SUPERMERCADO NACIONAL",
            Amount = new Money(245_000),
            OccurredAt = occurredAt,
            Kind = TxKind.Purchase,
            Status = TxStatus.Posted,
            Source = TxSource.Email,
            Fingerprint = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.Accounts.Add(cuenta);
        db.Transactions.Add(movimiento);
        await db.SaveChangesAsync();

        return movimiento.Id;
    }

    private MargenDbContext CrearContexto() => new(
        new DbContextOptionsBuilder<MargenDbContext>()
            .UseNpgsql(postgres.ConnectionString)
            .Options);
}
