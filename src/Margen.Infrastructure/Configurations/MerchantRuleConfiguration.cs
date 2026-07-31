using Margen.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Margen.Infrastructure.Configurations;

internal sealed class MerchantRuleConfiguration : IEntityTypeConfiguration<MerchantRule>
{
    public void Configure(EntityTypeBuilder<MerchantRule> builder)
    {
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Pattern).HasMaxLength(200);
        builder.Property(r => r.MatchKind).HasConversion<string>().HasMaxLength(32);

        builder.HasOne(r => r.Category)
            .WithMany()
            .HasForeignKey(r => r.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        // El mismo patrón con la misma forma de comparar es la misma regla.
        // Sin esto, corregir dos veces el mismo comercio deja dos reglas
        // idénticas compitiendo por peso.
        builder.HasIndex(r => new { r.Pattern, r.MatchKind }).IsUnique();

        // La cascada de clasificación recorre las reglas activas por peso
        // descendente en cada movimiento sin categoría.
        builder.HasIndex(r => new { r.IsActive, r.Weight }).IsDescending(false, true);
    }
}
