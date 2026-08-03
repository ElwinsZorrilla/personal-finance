using Margen.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Margen.Infrastructure.Configurations;

internal sealed class StatementProfileConfiguration : IEntityTypeConfiguration<StatementProfile>
{
    public void Configure(EntityTypeBuilder<StatementProfile> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasKey(p => p.Id);

        builder.Property(p => p.Name).HasMaxLength(120);
        builder.Property(p => p.DateFormat).HasMaxLength(32);
        builder.Property(p => p.Delimiter).HasMaxLength(4);
        builder.Property(p => p.Decimals).HasMaxLength(16);

        builder.HasOne(p => p.Account)
            .WithMany()
            .HasForeignKey(p => p.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        // El nombre lo elige una persona en una lista desplegable: dos perfiles
        // con el mismo nombre hacen imposible saber cuál se está usando.
        builder.HasIndex(p => p.Name).IsUnique();

        builder.ToTable(t =>
        {
            // Un perfil sin manera de leer el monto no puede importar nada, y
            // guardarlo deja una opción en la lista que falla al usarla.
            t.HasCheckConstraint(
                "CK_StatementProfiles_TieneMonto",
                "\"AmountColumn\" IS NOT NULL "
                + "OR \"DebitColumn\" IS NOT NULL "
                + "OR \"CreditColumn\" IS NOT NULL");
        });
    }
}
