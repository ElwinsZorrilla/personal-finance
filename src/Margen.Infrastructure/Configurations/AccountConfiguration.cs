using Margen.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Margen.Infrastructure.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Name).HasMaxLength(120);
        builder.Property(a => a.LastFour).HasMaxLength(4).IsFixedLength();
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(a => a.Currency).HasMaxLength(3).IsFixedLength();

        builder.HasIndex(a => a.IsActive);

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_Accounts_LastFour_Digits",
            "\"LastFour\" ~ '^[0-9]{4}$'"));
    }
}
