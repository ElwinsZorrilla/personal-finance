using Margen.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Margen.Infrastructure.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Name).HasMaxLength(80);
        builder.Property(c => c.Icon).HasMaxLength(64);
        builder.Property(c => c.Priority).HasConversion<string>().HasMaxLength(32);

        builder.HasIndex(c => c.Name).IsUnique();
    }
}
