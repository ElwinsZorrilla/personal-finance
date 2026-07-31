using Margen.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Margen.Infrastructure.Configurations;

internal sealed class AccessTokenConfiguration : IEntityTypeConfiguration<AccessToken>
{
    public void Configure(EntityTypeBuilder<AccessToken> builder)
    {
        builder.HasKey(t => t.Id);

        // SHA-256 en hexadecimal: 64 caracteres exactos.
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsFixedLength();
        builder.Property(t => t.Scopes).HasMaxLength(200);
        builder.Property(t => t.Label).HasMaxLength(120);

        builder.HasOne(t => t.Device)
            .WithMany(d => d!.Tokens)
            .HasForeignKey(t => t.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Toda petición autenticada entra por aquí: se busca el hash y se mira
        // si sigue vivo. Es la consulta más frecuente del servidor.
        builder.HasIndex(t => t.TokenHash).IsUnique();

        builder.HasIndex(t => new { t.DeviceId, t.RevokedAt });
    }
}
