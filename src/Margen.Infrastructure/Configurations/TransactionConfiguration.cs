using Margen.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Margen.Infrastructure.Configurations;

internal sealed class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.MerchantRaw).HasMaxLength(400);
        builder.Property(t => t.MerchantNormalized).HasMaxLength(200);
        builder.Property(t => t.Currency).HasMaxLength(3).IsFixedLength();
        builder.Property(t => t.Fingerprint).HasMaxLength(64);
        builder.Property(t => t.Notes).HasMaxLength(1000);
        builder.Property(t => t.ClassificationSource).HasMaxLength(64);

        builder.Property(t => t.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(t => t.Source).HasConversion<string>().HasMaxLength(32);
        builder.Property(t => t.Direction).HasConversion<string>().HasMaxLength(32);

        builder.Ignore(t => t.AffectsSpending);
        builder.Ignore(t => t.IsCredit);
        builder.Ignore(t => t.SpendingEffect);
        builder.Ignore(t => t.BalanceEffect);
        builder.Ignore(t => t.IsIncome);
        builder.Ignore(t => t.IsCategoryConfirmed);

        builder.HasOne(t => t.Account)
            .WithMany(a => a!.Transactions)
            .HasForeignKey(t => t.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.Category)
            .WithMany(c => c!.Transactions)
            .HasForeignKey(t => t.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.IncomingEmail)
            .WithMany(e => e!.Transactions)
            .HasForeignKey(t => t.IncomingEmailId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(t => t.RefundsTransaction)
            .WithMany()
            .HasForeignKey(t => t.RefundsTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(t => t.DuplicateOfTransaction)
            .WithMany()
            .HasForeignKey(t => t.DuplicateOfTransactionId)
            .OnDelete(DeleteBehavior.Restrict);

        // Segunda línea de defensa contra el duplicado, detrás del identificador
        // de mensaje del correo. Cubre el caso del banco que manda dos correos
        // distintos por la misma compra: el identificador difiere, la huella no.
        builder.HasIndex(t => t.Fingerprint).IsUnique();

        // El panel siempre pregunta lo mismo: los movimientos de un período,
        // del más reciente al más viejo.
        builder.HasIndex(t => t.OccurredAt).IsDescending();
        builder.HasIndex(t => new { t.AccountId, t.OccurredAt });
        builder.HasIndex(t => new { t.CategoryId, t.OccurredAt });
        builder.HasIndex(t => t.Status);

        // El panel separa ingresos de egresos, y la conciliación filtra por
        // dirección: sin índice, las dos consultas recorren la tabla entera.
        builder.HasIndex(t => new { t.Direction, t.OccurredAt });
        builder.HasIndex(t => t.MerchantNormalized);

        // La cascada pregunta esto por cada movimiento que entra: «qué
        // categorías confirmó el usuario en este comercio». Parcial, porque solo
        // interesan los confirmados y esos son una fracción de la tabla.
        builder.HasIndex(t => new { t.MerchantNormalized, t.CategoryId })
            .HasFilter("\"CategoryConfirmedAt\" IS NOT NULL");

        builder.ToTable(t =>
        {
            // El monto se guarda siempre positivo; la dirección la da Kind. Un
            // monto negativo que se cuele haría que un gasto sumara al saldo.
            t.HasCheckConstraint("CK_Transactions_Amount_NoNegativo", "\"Amount\" >= 0");

            t.HasCheckConstraint(
                "CK_Transactions_Confidence_Rango",
                "\"ConfidenceBasisPoints\" BETWEEN 0 AND 10000");

            // Un movimiento no puede ser devolución de sí mismo ni duplicado de
            // sí mismo: el motor entraría en un ciclo al seguir la referencia.
            t.HasCheckConstraint(
                "CK_Transactions_SinAutoreferencia",
                "\"RefundsTransactionId\" IS DISTINCT FROM \"Id\" "
                + "AND \"DuplicateOfTransactionId\" IS DISTINCT FROM \"Id\"");
        });
    }
}
