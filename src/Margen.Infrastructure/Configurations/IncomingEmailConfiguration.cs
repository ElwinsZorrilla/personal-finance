using Margen.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Margen.Infrastructure.Configurations;

internal sealed class IncomingEmailConfiguration : IEntityTypeConfiguration<IncomingEmail>
{
    public void Configure(EntityTypeBuilder<IncomingEmail> builder)
    {
        builder.HasKey(e => e.Id);

        builder.Property(e => e.MessageId).HasMaxLength(400);
        builder.Property(e => e.BodyHash).HasMaxLength(64).IsFixedLength();
        builder.Property(e => e.Sender).HasMaxLength(320);
        builder.Property(e => e.Subject).HasMaxLength(500);

        // El cuerpo de un correo del banco no tiene techo conocido. Un límite
        // inventado trunca el original y deja el reproceso sin material.
        builder.Property(e => e.Body).HasColumnType("text");

        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(e => e.ParserName).HasMaxLength(80);
        builder.Property(e => e.FailureReason).HasMaxLength(2000);

        // Las dos defensas contra procesar dos veces el mismo correo. Nacen
        // aquí, en la migración inicial, y no en la Fase 6 cuando se usan:
        // añadir un índice único a una tabla que ya acumuló duplicados es una
        // migración que falla en el servidor y no en la máquina de quien la
        // escribió.
        builder.HasIndex(e => e.MessageId).IsUnique();
        builder.HasIndex(e => e.BodyHash).IsUnique();

        builder.HasIndex(e => new { e.Status, e.ReceivedAt });
    }
}
