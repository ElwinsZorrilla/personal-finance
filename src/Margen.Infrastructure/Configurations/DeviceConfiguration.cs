using Margen.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Margen.Infrastructure.Configurations;

internal sealed class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> builder)
    {
        builder.HasKey(d => d.Id);

        builder.Property(d => d.Name).HasMaxLength(120);

        // Una clave pública P-256 en SubjectPublicKeyInfo DER son 91 bytes;
        // en Base64, 124 caracteres. El margen es para que la columna sobreviva
        // a una curva distinta sin una migración.
        builder.Property(d => d.PublicKeySpki).HasMaxLength(512);
        builder.Property(d => d.PublicKeyFingerprint).HasMaxLength(64).IsFixedLength();

        builder.Ignore(d => d.IsActive);

        // El mismo teléfono no se da de alta dos veces.
        builder.HasIndex(d => d.PublicKeyFingerprint).IsUnique();
    }
}
