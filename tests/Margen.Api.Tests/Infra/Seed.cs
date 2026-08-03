using Margen.Api.Budget;
using Margen.Domain;
using Margen.Domain.Entities;
using Margen.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Margen.Api.Tests.Infra;

/// <summary>
/// Siembra un escenario completo en una base limpia.
/// </summary>
/// <remarks>
/// Cada llamada crea su propio juego de identificadores y **borra lo anterior**.
/// Las pruebas de endpoint comparten el contenedor, así que sin el borrado una
/// dejaría movimientos que otra contaría, y el fallo aparecería en la prueba
/// equivocada según el orden de ejecución.
/// </remarks>
internal sealed class Seed
{
    public Guid PeriodId { get; private set; }

    public Guid CheckingId { get; private set; }

    public Guid CashId { get; private set; }

    public Guid CreditId { get; private set; }

    public Guid FoodId { get; private set; }

    public Guid RentId { get; private set; }

    public Guid FunId { get; private set; }

    public DateOnly Start { get; private set; }

    public DateOnly End { get; private set; }

    /// <summary>
    /// Construye un período que **contiene el día de hoy**, porque los
    /// endpoints buscan el período abierto que contiene hoy. Fijar fechas de
    /// 2026 haría que las pruebas pasaran hoy y fallaran el año que viene.
    /// </summary>
    public static async Task<Seed> PlantAsync(string connectionString)
    {
        var options = new DbContextOptionsBuilder<MargenDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var db = new MargenDbContext(options);

        // Orden inverso a las claves foráneas.
        await db.Alerts.ExecuteDeleteAsync();
        await db.Transactions.ExecuteDeleteAsync();
        await db.CategoryBudgets.ExecuteDeleteAsync();
        await db.MerchantRules.ExecuteDeleteAsync();
        await db.RecurringPayments.ExecuteDeleteAsync();
        await db.BudgetPeriods.ExecuteDeleteAsync();
        await db.Categories.ExecuteDeleteAsync();
        await db.IncomingEmails.ExecuteDeleteAsync();
        await db.Accounts.ExecuteDeleteAsync();

        DateOnly today = LocalTime.LocalDateOf(DateTime.UtcNow);
        DateTime now = DateTime.UtcNow;

        var seed = new Seed
        {
            PeriodId = Guid.CreateVersion7(),
            CheckingId = Guid.CreateVersion7(),
            CashId = Guid.CreateVersion7(),
            CreditId = Guid.CreateVersion7(),
            FoodId = Guid.CreateVersion7(),
            RentId = Guid.CreateVersion7(),
            FunId = Guid.CreateVersion7(),

            // Ciclo de 30 días con hoy justo a la mitad, para que el ritmo y el
            // disponible diario tengan algo que decir.
            Start = today.AddDays(-14),
            End = today.AddDays(15),
        };

        db.Accounts.AddRange(
            new Account
            {
                Id = seed.CheckingId,
                Name = "Cuenta de nómina",
                LastFour = "1234",
                Kind = AccountKind.Checking,
                Balance = Money.FromUnits(60_000),
                CreatedAt = now,
                UpdatedAt = now,
            },
            new Account
            {
                Id = seed.CashId,
                Name = "Efectivo",
                LastFour = "0000",
                Kind = AccountKind.Cash,
                Balance = Money.FromUnits(5_000),
                CreatedAt = now,
                UpdatedAt = now,
            },
            new Account
            {
                Id = seed.CreditId,
                Name = "Tarjeta",
                LastFour = "9876",
                Kind = AccountKind.Credit,

                // Saldo negativo: se debe. Entra en la reserva de tarjetas.
                Balance = Money.FromUnits(-12_000),
                CreditLimit = Money.FromUnits(100_000),
                CreatedAt = now,
                UpdatedAt = now,
            });

        db.Categories.AddRange(
            new Category
            {
                Id = seed.FoodId,
                Name = "Comida",
                Priority = Priority.Flexible,
                CreatedAt = now,
            },
            new Category
            {
                Id = seed.RentId,
                Name = "Alquiler",
                Priority = Priority.Essential,
                CreatedAt = now,
            },
            new Category
            {
                Id = seed.FunId,
                Name = "Ocio",
                Priority = Priority.Optional,
                CreatedAt = now,
            });

        db.BudgetPeriods.Add(new Domain.Entities.BudgetPeriod
        {
            Id = seed.PeriodId,
            StartDate = seed.Start,
            EndDate = seed.End,
            ExpectedIncome = Money.FromUnits(80_000),
            ActualIncome = Money.FromUnits(80_000),
            SafetyFund = Money.FromUnits(10_000),
            CommittedSavings = Money.FromUnits(5_000),
            CreatedAt = now,
        });

        db.CategoryBudgets.AddRange(
            Budget(seed.PeriodId, seed.FoodId, 20_000, now),
            Budget(seed.PeriodId, seed.RentId, 25_000, now),
            Budget(seed.PeriodId, seed.FunId, 6_000, now));

        db.RecurringPayments.Add(new RecurringPayment
        {
            Id = Guid.CreateVersion7(),
            Label = "Alquiler",
            CategoryId = seed.RentId,
            ExpectedAmount = Money.FromUnits(25_000),
            Cadence = Cadence.Monthly,
            DayOfCycle = 1,
            NextDueDate = today.AddDays(3),
            Priority = Priority.Essential,
            CreatedAt = now,
            UpdatedAt = now,
        });

        await db.SaveChangesAsync();
        return seed;
    }

    private static CategoryBudget Budget(Guid period, Guid category, long units, DateTime now) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            BudgetPeriodId = period,
            CategoryId = category,
            Allocated = Money.FromUnits(units),
            CreatedAt = now,
            UpdatedAt = now,
        };

    /// <summary>Añade un movimiento con el instante UTC exacto que se le pida.</summary>
    public static async Task<Guid> AddTransactionAsync(
        string connectionString,
        Guid accountId,
        Guid? categoryId,
        long cents,
        DateTime occurredAtUtc,
        TxKind kind = TxKind.Purchase,
        TxStatus status = TxStatus.Posted,
        Guid? refunds = null,
        string merchant = "SUPERMERCADO NACIONAL")
    {
        var options = new DbContextOptionsBuilder<MargenDbContext>()
            .UseNpgsql(connectionString)
            .Options;

        await using var db = new MargenDbContext(options);

        var transaction = new Transaction
        {
            Id = Guid.CreateVersion7(),
            AccountId = accountId,
            CategoryId = categoryId,
            MerchantRaw = merchant,
            MerchantNormalized = merchant,
            Amount = new Money(cents),
            OccurredAt = occurredAtUtc,
            Kind = kind,
            Status = status,
            Source = TxSource.Email,
            Direction = Directions.Of(kind),
            RefundsTransactionId = refunds,
            Fingerprint = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.Transactions.Add(transaction);
        await db.SaveChangesAsync();

        return transaction.Id;
    }
}
