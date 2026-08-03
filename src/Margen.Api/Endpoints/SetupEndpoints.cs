using Margen.Api.Auth;
using Margen.Api.Budget;
using Margen.Api.Contracts;
using Margen.Classify;
using Margen.Domain;
using Margen.Domain.Entities;
using Margen.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Margen.Api.Endpoints;

/// <summary>
/// El arranque en frío: poner en pie una instalación recién desplegada.
/// </summary>
/// <remarks>
/// Existe porque hasta ahora no existía, y eso solo se ve al desplegar por
/// primera vez: todas las pruebas siembran sus propios datos y producción
/// nacía **vacía**. Sin cuentas, la ingesta no tiene dónde colgar los
/// movimientos; sin categorías, la cascada no tiene qué asignar; sin período
/// abierto, el panel no puede calcular nada.
///
/// **Nada de esto se crea solo al arrancar el servidor.** Un saldo inicial
/// inventado entra directo en la fórmula del dinero seguro, y una cuenta que
/// nadie pidió es peor que ninguna. Lo único que se siembra sin preguntar son
/// las categorías, que no llevan cifras.
///
/// Todo es **idempotente**: llamar dos veces no duplica nada. Es lo que permite
/// dejar estas peticiones escritas en un documento y que alguien las repita sin
/// miedo.
/// </remarks>
public static class SetupEndpoints
{
    public static IEndpointRouteBuilder MapSetupEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder group = app.MapGroup("/setup").WithTags("Puesta en marcha");

        group.MapGet("/status", StatusAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("GetSetupStatus")
            .Produces<SetupStatusView>();

        group.MapPost("/categories/defaults", SeedCategoriesAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("SeedDefaultCategories")
            .Produces<SetupStatusView>();

        group.MapPost("/accounts", CreateAccountAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("CreateAccount")
            .Produces<AccountView>(StatusCodes.Status201Created);

        group.MapPost("/periods", OpenPeriodAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("OpenPeriod")
            .Produces<PeriodOpenedView>(StatusCodes.Status201Created);

        return app;
    }

    /// <summary>
    /// Qué falta para que la aplicación pueda funcionar.
    /// </summary>
    /// <remarks>
    /// Es lo primero que se llama y lo que se vuelve a llamar al final. Dice qué
    /// falta en lugar de un sí o un no: «no está lista» sin decir por qué obliga
    /// a adivinar.
    /// </remarks>
    private static async Task<IResult> StatusAsync(
        [FromServices] MargenDbContext db,
        [FromServices] TimeProvider clock,
        CancellationToken cancellationToken)
    {
        int cuentas = await db.Accounts.CountAsync(a => a.IsActive, cancellationToken)
            .ConfigureAwait(false);
        int categorias = await db.Categories.CountAsync(c => c.IsActive, cancellationToken)
            .ConfigureAwait(false);

        DateOnly hoy = LocalTime.LocalDateOf(clock.GetUtcNow().UtcDateTime);

        bool periodo = await db.BudgetPeriods
            .AnyAsync(p => !p.IsClosed && p.StartDate <= hoy && p.EndDate >= hoy, cancellationToken)
            .ConfigureAwait(false);

        // Correos que llegaron antes de que existiera su cuenta. No se pierden:
        // quedan guardados con el motivo, y `--reprocesar` los vuelve a mirar
        // cuando la cuenta exista.
        int enEspera = await db.IncomingEmails
            .CountAsync(e => e.Status == EmailStatus.Received, cancellationToken)
            .ConfigureAwait(false);

        var falta = new List<string>();
        if (cuentas == 0) falta.Add("No hay ninguna cuenta. Los movimientos no tienen dónde colgarse.");
        if (categorias == 0) falta.Add("No hay categorías. La clasificación no tiene qué asignar.");
        if (!periodo) falta.Add("No hay un período abierto que contenga hoy. El panel no puede calcular.");

        return Results.Ok(new SetupStatusView(
            falta.Count == 0, cuentas, categorias, periodo, enEspera, falta));
    }

    /// <summary>
    /// Crea las categorías que el clasificador ya sabe reconocer.
    /// </summary>
    /// <remarks>
    /// Se siembran sin preguntar porque **no llevan ninguna cifra**: son
    /// nombres y una prioridad, y equivocarse en una se arregla renombrándola.
    /// Los nombres son exactamente los de <see cref="CategoryNames"/>: el
    /// clasificador local devuelve un nombre y alguien tiene que haberlo creado
    /// con ese nombre exacto, o la cascada nunca acierta.
    ///
    /// Idempotente: las que ya existen no se tocan ni se duplican.
    /// </remarks>
    private static async Task<IResult> SeedCategoriesAsync(
        [FromServices] MargenDbContext db,
        [FromServices] TimeProvider clock,
        CancellationToken cancellationToken)
    {
        (string Nombre, Priority Prioridad)[] predeterminadas =
        [
            (CategoryNames.Supermercado, Priority.Essential),
            (CategoryNames.Servicios, Priority.Essential),
            (CategoryNames.Salud, Priority.Essential),
            (CategoryNames.Transporte, Priority.Important),
            (CategoryNames.Suscripciones, Priority.Flexible),
            (CategoryNames.Restaurantes, Priority.Flexible),
            (CategoryNames.Efectivo, Priority.Flexible),
        ];

        HashSet<string> existentes =
        [
            .. await db.Categories.Select(c => c.Name).ToListAsync(cancellationToken)
                .ConfigureAwait(false),
        ];

        DateTime now = clock.GetUtcNow().UtcDateTime;

        foreach ((string nombre, Priority prioridad) in predeterminadas)
        {
            if (existentes.Contains(nombre)) continue;

            db.Categories.Add(new Category
            {
                Id = Guid.CreateVersion7(),
                Name = nombre,
                Priority = prioridad,
                CreatedAt = now,
            });
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return await StatusAsync(db, clock, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Da de alta una cuenta o una tarjeta.
    /// </summary>
    /// <remarks>
    /// Los **cuatro últimos dígitos** son lo único que trae el correo del banco
    /// para saber de qué cuenta habla, así que son obligatorios y únicos entre
    /// las cuentas activas: con dos cuentas terminadas en lo mismo, un
    /// movimiento iría a una de las dos según el orden de la consulta.
    ///
    /// El **saldo lo pone una persona**. Es la única cifra de todo el sistema
    /// que no sale de un correo ni de un cálculo, y entra directa en la fórmula
    /// del dinero seguro: inventarla sería mentir sobre cuánto hay.
    /// </remarks>
    private static async Task<IResult> CreateAccountAsync(
        [FromBody] CreateAccountRequest request,
        [FromServices] MargenDbContext db,
        [FromServices] TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ApiResults.BadRequest("La cuenta necesita un nombre.");
        }

        if (request.LastFour?.Length != 4 || !request.LastFour.All(char.IsAsciiDigit))
        {
            return ApiResults.BadRequest(
                "Los últimos cuatro dígitos son exactamente cuatro dígitos.");
        }

        if (!Enum.TryParse(request.Kind, ignoreCase: true, out AccountKind kind))
        {
            return ApiResults.BadRequest(
                $"Tipo desconocido: «{request.Kind}». Vale Checking, Savings, Credit o Cash.");
        }

        Account? existente = await db.Accounts
            .FirstOrDefaultAsync(a => a.LastFour == request.LastFour && a.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (existente is not null)
        {
            // Idempotente: repetir la misma alta devuelve la que hay en vez de
            // crear una segunda que compita por los mismos correos.
            return Results.Ok(ToView(existente));
        }

        var cuenta = new Account
        {
            Id = Guid.CreateVersion7(),
            Name = request.Name.Trim(),
            LastFour = request.LastFour,
            Kind = kind,
            Balance = new Money(request.BalanceCents),
            CreditLimit = request.CreditLimitCents is long limite ? new Money(limite) : null,
            CreatedAt = clock.GetUtcNow().UtcDateTime,
            UpdatedAt = clock.GetUtcNow().UtcDateTime,
        };

        db.Accounts.Add(cuenta);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Created($"/setup/accounts/{cuenta.Id}", ToView(cuenta));
    }

    /// <summary>
    /// Abre el período que contiene hoy.
    /// </summary>
    /// <remarks>
    /// El período **no va del 1 al 30**: va de un ingreso al siguiente, porque
    /// ese es el ciclo real del dinero de una persona asalariada. Se da el día
    /// de cobro y el período se calcula alrededor de hoy.
    ///
    /// Idempotente: si ya hay uno abierto que contiene hoy, se devuelve ese.
    /// Abrir dos que se solapan haría que el panel eligiera uno de los dos según
    /// el orden de la consulta.
    /// </remarks>
    private static async Task<IResult> OpenPeriodAsync(
        [FromBody] OpenPeriodRequest request,
        [FromServices] MargenDbContext db,
        [FromServices] TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.PayDay is < 1 or > 31)
        {
            return ApiResults.BadRequest("El día de cobro va de 1 a 31.");
        }

        if (request.ExpectedIncomeCents <= 0)
        {
            return ApiResults.BadRequest("El ingreso esperado tiene que ser mayor que cero.");
        }

        DateOnly hoy = LocalTime.LocalDateOf(clock.GetUtcNow().UtcDateTime);

        BudgetPeriod? abierto = await db.BudgetPeriods
            .FirstOrDefaultAsync(
                p => !p.IsClosed && p.StartDate <= hoy && p.EndDate >= hoy, cancellationToken)
            .ConfigureAwait(false);

        if (abierto is not null)
        {
            return Results.Ok(new PeriodOpenedView(
                abierto.Id, abierto.StartDate, abierto.EndDate, abierto.ExpectedIncome.Cents, false));
        }

        (DateOnly inicio, DateOnly fin) = CycleAround(hoy, request.PayDay);

        var periodo = new BudgetPeriod
        {
            Id = Guid.CreateVersion7(),
            StartDate = inicio,
            EndDate = fin,
            ExpectedIncome = new Money(request.ExpectedIncomeCents),
            SafetyFund = new Money(request.SafetyFundCents ?? 0),
            CommittedSavings = new Money(request.CommittedSavingsCents ?? 0),
            CreatedAt = clock.GetUtcNow().UtcDateTime,
        };

        db.BudgetPeriods.Add(periodo);
        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Created(
            $"/setup/periods/{periodo.Id}",
            new PeriodOpenedView(
                periodo.Id, periodo.StartDate, periodo.EndDate, periodo.ExpectedIncome.Cents, true));
    }

    /// <summary>
    /// El ciclo de cobro que contiene un día.
    /// </summary>
    /// <remarks>
    /// Empieza el día de cobro de este mes si ya pasó, o el del mes anterior si
    /// todavía no. Termina el día antes del cobro siguiente.
    ///
    /// Un día 31 en un mes de 30 se corre al último día del mes. Sin eso, un
    /// cobro el 31 dejaría sin período los meses de treinta días —y febrero
    /// entero—, que es la clase de error que aparece una vez al año y parece
    /// magia negra.
    /// </remarks>
    internal static (DateOnly Start, DateOnly End) CycleAround(DateOnly today, int payDay)
    {
        DateOnly EsteMes(int año, int mes)
        {
            int dia = Math.Min(payDay, DateTime.DaysInMonth(año, mes));
            return new DateOnly(año, mes, dia);
        }

        DateOnly inicio = EsteMes(today.Year, today.Month);

        if (inicio > today)
        {
            DateOnly anterior = today.AddMonths(-1);
            inicio = EsteMes(anterior.Year, anterior.Month);
        }

        DateOnly siguiente = inicio.AddMonths(1);
        DateOnly fin = EsteMes(siguiente.Year, siguiente.Month).AddDays(-1);

        return (inicio, fin);
    }

    private static AccountView ToView(Account a) => new(
        a.Id, a.Name, a.LastFour, a.Kind.ToString(), a.Balance.Cents, a.CreditLimit?.Cents);
}
