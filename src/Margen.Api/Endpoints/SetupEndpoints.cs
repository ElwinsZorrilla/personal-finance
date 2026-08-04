using Margen.Api.Auth;
using Margen.Api.Budget;
using Margen.Api.Contracts;
using Margen.Budget;
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

        group.MapGet("/categories", ListCategoriesAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("ListCategories")
            .Produces<IReadOnlyList<CategoryView>>();

        group.MapGet("/accounts", ListAccountsAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("ListAccounts")
            .Produces<IReadOnlyList<AccountView>>();

        group.MapPut("/accounts/{id:guid}", UpdateAccountAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("UpdateAccount")
            .Produces<AccountView>();

        group.MapDelete("/accounts/{id:guid}", DeactivateAccountAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("DeactivateAccount")
            .Produces<AccountView>();

        group.MapGet("/periods/current", CurrentPeriodAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("GetCurrentPeriod")
            .Produces<PeriodOpenedView>();

        group.MapPut("/periods/current", UpdatePeriodAsync)
            .RequireAuthorization(ScopePolicies.Full)
            .WithName("UpdateCurrentPeriod")
            .Produces<PeriodOpenedView>();

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

        Outcome<PaySchedule> calendario = PaySchedule.Of(request.PayDays ?? []);

        if (!calendario.IsComputed)
        {
            return ApiResults.BadRequest(calendario.Reason!);
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

        BudgetCycle ciclo = calendario.Value.CycleAround(hoy);
        (DateOnly inicio, DateOnly fin) = (ciclo.Start, ciclo.End);

        var periodo = new BudgetPeriod
        {
            Id = Guid.CreateVersion7(),
            StartDate = inicio,
            EndDate = fin,
            PayDays = [.. calendario.Value.Days],
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
    /// Las categorías, ordenadas por prioridad y luego por nombre.
    /// </summary>
    /// <remarks>
    /// Existe para poder **cambiar la categoría de un movimiento**: sin los
    /// identificadores, la app puede enseñar el nombre que le llega pero no
    /// ofrecer otro. Era el hueco que dejaba la pantalla de movimientos en solo
    /// lectura.
    ///
    /// El orden pone primero lo que no se puede recortar. En una lista para
    /// elegir, eso deja arriba lo que casi nunca se toca y abajo lo flexible,
    /// que es donde de verdad se corrige una clasificación.
    /// </remarks>
    private static async Task<IResult> ListCategoriesAsync(
        [FromServices] MargenDbContext db,
        CancellationToken cancellationToken)
    {
        List<Category> categorias = await db.Categories
            .AsNoTracking()
            .OrderBy(c => c.Priority)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(categorias
            .Select(c => new CategoryView(
                c.Id, c.Name, c.Priority.ToString(), c.Icon, c.IsSystem))
            .ToList());
    }

    /// <summary>Las cuentas activas, en el orden en que se dieron de alta.</summary>
    private static async Task<IResult> ListAccountsAsync(
        [FromServices] MargenDbContext db,
        CancellationToken cancellationToken)
    {
        List<Account> cuentas = await db.Accounts
            .AsNoTracking()
            .Where(a => a.IsActive)
            .OrderBy(a => a.CreatedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Results.Ok(cuentas.Select(ToView).ToList());
    }

    /// <summary>
    /// Corrige una cuenta: nombre, saldo y límite.
    /// </summary>
    /// <remarks>
    /// **Los cuatro últimos dígitos y el tipo no se cambian aquí.** No es
    /// pereza: son la identidad de la cuenta frente a los correos del banco y
    /// frente a los movimientos ya colgados. Cambiarlos convertiría esta cuenta
    /// en otra distinta y dejaría su historial atado a una identidad que ya no
    /// existe. Para eso se da de baja esta y se crea otra.
    ///
    /// El saldo sí, y es lo que más se va a usar: es la única cifra del sistema
    /// que escribe una persona, y se desvía de la realidad en cuanto un
    /// movimiento no llega por correo.
    /// </remarks>
    private static async Task<IResult> UpdateAccountAsync(
        Guid id,
        [FromBody] UpdateAccountRequest request,
        [FromServices] MargenDbContext db,
        [FromServices] TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return ApiResults.BadRequest("La cuenta necesita un nombre.");
        }

        Account? cuenta = await db.Accounts
            .FirstOrDefaultAsync(a => a.Id == id && a.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (cuenta is null)
        {
            return ApiResults.NotFound("No hay ninguna cuenta activa con ese identificador.");
        }

        cuenta.Name = request.Name.Trim();
        cuenta.Balance = new Money(request.BalanceCents);
        cuenta.CreditLimit = request.CreditLimitCents is long limite ? new Money(limite) : null;
        cuenta.UpdatedAt = clock.GetUtcNow().UtcDateTime;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(ToView(cuenta));
    }

    /// <summary>
    /// Da de baja una cuenta. **No la borra.**
    /// </summary>
    /// <remarks>
    /// Borrarla se llevaría por delante sus movimientos, y con ellos el gasto
    /// de los períodos ya cerrados: la base histórica pondera los tres
    /// anteriores y pasaría a calcularse sobre un pasado que cambió.
    ///
    /// Inactiva significa que no cuenta para el dinero líquido y que los correos
    /// nuevos con esos cuatro dígitos ya no encuentran dónde colgarse. Lo que
    /// pasó, pasó.
    ///
    /// **No se deja dar de baja la última.** Sin ninguna cuenta activa el panel
    /// no puede calcular, y la app volvería a la pantalla de puesta en marcha
    /// sin que nadie lo hubiera pedido.
    /// </remarks>
    private static async Task<IResult> DeactivateAccountAsync(
        Guid id,
        [FromServices] MargenDbContext db,
        [FromServices] TimeProvider clock,
        CancellationToken cancellationToken)
    {
        Account? cuenta = await db.Accounts
            .FirstOrDefaultAsync(a => a.Id == id && a.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (cuenta is null)
        {
            return ApiResults.NotFound("No hay ninguna cuenta activa con ese identificador.");
        }

        int activas = await db.Accounts
            .CountAsync(a => a.IsActive, cancellationToken)
            .ConfigureAwait(false);

        if (activas <= 1)
        {
            return ApiResults.BadRequest(
                "Es la única cuenta activa. Sin ninguna, el panel no puede calcular.");
        }

        cuenta.IsActive = false;
        cuenta.UpdatedAt = clock.GetUtcNow().UtcDateTime;

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(ToView(cuenta));
    }

    /// <summary>El período abierto que contiene hoy.</summary>
    private static async Task<IResult> CurrentPeriodAsync(
        [FromServices] MargenDbContext db,
        [FromServices] TimeProvider clock,
        CancellationToken cancellationToken)
    {
        DateOnly hoy = LocalTime.LocalDateOf(clock.GetUtcNow().UtcDateTime);

        BudgetPeriod? abierto = await db.BudgetPeriods
            .AsNoTracking()
            .FirstOrDefaultAsync(
                p => !p.IsClosed && p.StartDate <= hoy && p.EndDate >= hoy, cancellationToken)
            .ConfigureAwait(false);

        return abierto is null
            ? ApiResults.NotFound("No hay ningún período abierto que contenga hoy.")
            : Results.Ok(ToView(abierto, created: false));
    }

    /// <summary>
    /// Corrige el período abierto: días de cobro, ingreso y apartados.
    /// </summary>
    /// <remarks>
    /// Existe porque <see cref="OpenPeriodAsync"/> es idempotente y devuelve el
    /// que hay en vez de cambiarlo. Sin esto, un período abierto con el
    /// calendario equivocado no se puede arreglar hasta que termine, y en
    /// producción pasó exactamente eso: se abrió con un solo cobro antes de que
    /// existieran los dos.
    ///
    /// **Las fechas se recalculan.** Un período que dice cobrar dos veces al mes
    /// y abarca treinta días no es un período con una etiqueta mal puesta: es
    /// una cifra de gasto diario equivocada. Cambiar el calendario sin mover las
    /// fechas dejaría la mentira intacta y encima con aspecto de estar arreglada.
    ///
    /// La consecuencia hay que decirla: al acortarse, los movimientos que
    /// quedan antes del inicio nuevo **salen de este período**. Es lo correcto
    /// —pertenecen al ciclo anterior— y por eso la respuesta devuelve las fechas
    /// resultantes, para que quien llama pueda enseñarlas antes de dar por buena
    /// la corrección.
    /// </remarks>
    private static async Task<IResult> UpdatePeriodAsync(
        [FromBody] OpenPeriodRequest request,
        [FromServices] MargenDbContext db,
        [FromServices] TimeProvider clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Outcome<PaySchedule> calendario = PaySchedule.Of(request.PayDays ?? []);

        if (!calendario.IsComputed)
        {
            return ApiResults.BadRequest(calendario.Reason!);
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

        if (abierto is null)
        {
            return ApiResults.NotFound("No hay ningún período abierto que contenga hoy.");
        }

        BudgetCycle ciclo = calendario.Value.CycleAround(hoy);

        // Dos períodos no pueden empezar el mismo día: hay un índice único que
        // lo impide. Se comprueba antes para responder con una frase en vez de
        // con el error de la base.
        bool choca = await db.BudgetPeriods
            .AnyAsync(
                p => p.Id != abierto.Id && p.StartDate == ciclo.Start, cancellationToken)
            .ConfigureAwait(false);

        if (choca)
        {
            return ApiResults.BadRequest(
                $"Ya hay otro período que empieza el {ciclo.Start:dd/MM/yyyy}.");
        }

        abierto.PayDays = [.. calendario.Value.Days];
        abierto.StartDate = ciclo.Start;
        abierto.EndDate = ciclo.End;
        abierto.ExpectedIncome = new Money(request.ExpectedIncomeCents);
        abierto.SafetyFund = new Money(request.SafetyFundCents ?? 0);
        abierto.CommittedSavings = new Money(request.CommittedSavingsCents ?? 0);

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Results.Ok(ToView(abierto, created: false));
    }

    private static PeriodOpenedView ToView(BudgetPeriod p, bool created) => new(
        p.Id, p.StartDate, p.EndDate, p.ExpectedIncome.Cents, created);

    private static AccountView ToView(Account a) => new(
        a.Id, a.Name, a.LastFour, a.Kind.ToString(), a.Balance.Cents, a.CreditLimit?.Cents);
}
