using Margen.Domain;
using Margen.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Margen.Infrastructure;

public class MargenDbContext(DbContextOptions<MargenDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<BudgetPeriod> BudgetPeriods => Set<BudgetPeriod>();

    public DbSet<CategoryBudget> CategoryBudgets => Set<CategoryBudget>();

    public DbSet<MerchantRule> MerchantRules => Set<MerchantRule>();

    public DbSet<RecurringPayment> RecurringPayments => Set<RecurringPayment>();

    public DbSet<IncomingEmail> IncomingEmails => Set<IncomingEmail>();

    public DbSet<Alert> Alerts => Set<Alert>();

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<DeviceChallenge> DeviceChallenges => Set<DeviceChallenge>();

    public DbSet<AccessToken> AccessTokens => Set<AccessToken>();

    /// <summary>Cómo se lee el CSV de cada banco. Uno por banco.</summary>
    public DbSet<StatementProfile> StatementProfiles => Set<StatementProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MargenDbContext).Assembly);
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);

        // Money es un long de centavos y su columna es bigint. Se declara como
        // convención y no configuración por configuración: una propiedad
        // monetaria que alguien añada mañana sin acordarse de mapearla queda
        // igual de protegida. Es la diferencia entre una regla y una costumbre.
        configurationBuilder.Properties<Money>()
            .HaveConversion<MoneyConverter>()
            .HaveColumnType("bigint");

        configurationBuilder.Properties<Money?>()
            .HaveConversion<NullableMoneyConverter>()
            .HaveColumnType("bigint");

        // Todo instante es timestamptz. Sin esto, Npgsql mapea DateTime según
        // su Kind y una fecha construida sin Kind explícito termina en una
        // columna sin zona: la hora se guarda tal cual y el punto en la línea
        // del tiempo se pierde.
        configurationBuilder.Properties<DateTime>()
            .HaveColumnType("timestamp with time zone");

        configurationBuilder.Properties<DateTime?>()
            .HaveColumnType("timestamp with time zone");

        configurationBuilder.Properties<DateOnly>().HaveColumnType("date");
        configurationBuilder.Properties<DateOnly?>().HaveColumnType("date");

        // Un texto sin longitud declarada en Postgres es `text`, que no tiene
        // límite. Los campos con forma conocida la declaran en su
        // configuración; este es el techo de los demás.
        configurationBuilder.Properties<string>().HaveMaxLength(1024);
    }
}

internal sealed class MoneyConverter() : ValueConverter<Money, long>(
    money => money.Cents,
    cents => new Money(cents));

internal sealed class NullableMoneyConverter() : ValueConverter<Money?, long?>(
    money => money == null ? null : money.Value.Cents,
    cents => cents == null ? null : new Money(cents.Value));
