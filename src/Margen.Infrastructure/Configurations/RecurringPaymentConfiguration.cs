using Margen.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Margen.Infrastructure.Configurations;

internal sealed class RecurringPaymentConfiguration : IEntityTypeConfiguration<RecurringPayment>
{
    public void Configure(EntityTypeBuilder<RecurringPayment> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Label).HasMaxLength(120);
        builder.Property(p => p.Cadence).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.Priority).HasConversion<string>().HasMaxLength(32);

        builder.HasOne(p => p.Category)
            .WithMany()
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.Account)
            .WithMany()
            .HasForeignKey(p => p.AccountId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(p => new { p.IsActive, p.NextDueDate });

        builder.ToTable(t =>
        {
            t.HasCheckConstraint(
                "CK_RecurringPayments_DiaDelCiclo",
                "\"DayOfCycle\" BETWEEN 1 AND 31");

            // Un compromiso de monto cero no es un compromiso; casi siempre es
            // un formulario que se guardó a medias.
            t.HasCheckConstraint(
                "CK_RecurringPayments_MontoPositivo",
                "\"ExpectedAmount\" > 0");
        });
    }
}
