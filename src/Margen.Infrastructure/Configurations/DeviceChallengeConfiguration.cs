using Margen.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Margen.Infrastructure.Configurations;

internal sealed class DeviceChallengeConfiguration : IEntityTypeConfiguration<DeviceChallenge>
{
    public void Configure(EntityTypeBuilder<DeviceChallenge> builder)
    {
        builder.HasKey(c => c.Id);

        // Treinta y dos bytes en Base64 son 44 caracteres.
        builder.Property(c => c.Nonce).HasMaxLength(64);

        builder.HasOne(c => c.Device)
            .WithMany(d => d!.Challenges)
            .HasForeignKey(c => c.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Único de verdad: el canje busca por nonce y el nonce tiene que
        // identificar un solo reto en toda la tabla. Con un índice por
        // dispositivo, dos retos iguales de dos dispositivos distintos harían
        // que la búsqueda dependiera de a quién dice ser el que llama.
        builder.HasIndex(c => c.Nonce).IsUnique();

        // La limpieza de retos vencidos barre por esta columna.
        builder.HasIndex(c => c.ExpiresAt);
    }
}
