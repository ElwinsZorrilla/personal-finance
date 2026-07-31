using Margen.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Margen.Infrastructure.Configurations;

internal sealed class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(40);
        builder.Property(a => a.Title).HasMaxLength(200);
        builder.Property(a => a.Detail).HasMaxLength(1000);
        builder.Property(a => a.DedupeKey).HasMaxLength(200);

        builder.Ignore(a => a.IsResolved);

        builder.HasOne(a => a.Transaction)
            .WithMany()
            .HasForeignKey(a => a.TransactionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.IncomingEmail)
            .WithMany()
            .HasForeignKey(a => a.IncomingEmailId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.RecurringPayment)
            .WithMany()
            .HasForeignKey(a => a.RecurringPaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        // Único solo mientras esté sin resolver. Una factura que no llegó
        // genera una alerta, no una por cada vez que el worker mira el reloj;
        // pero si se resuelve y el mes siguiente vuelve a faltar, tiene que
        // poder avisar otra vez.
        builder.HasIndex(a => a.DedupeKey)
            .IsUnique()
            .HasFilter("\"ResolvedAt\" IS NULL");

        builder.HasIndex(a => new { a.ResolvedAt, a.IsUrgent, a.CreatedAt });
    }
}
