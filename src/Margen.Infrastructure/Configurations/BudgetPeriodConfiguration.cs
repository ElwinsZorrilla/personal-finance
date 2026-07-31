using Margen.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Margen.Infrastructure.Configurations;

internal sealed class BudgetPeriodConfiguration : IEntityTypeConfiguration<BudgetPeriod>
{
    public void Configure(EntityTypeBuilder<BudgetPeriod> builder)
    {
        builder.HasKey(p => p.Id);

        // Dos períodos no pueden empezar el mismo día. Sin esta restricción, un
        // cierre de período que se ejecuta dos veces crea dos períodos
        // solapados y el motor reparte el mismo ingreso dos veces.
        builder.HasIndex(p => p.StartDate).IsUnique();

        builder.ToTable(t => t.HasCheckConstraint(
            "CK_BudgetPeriods_Order",
            "\"EndDate\" >= \"StartDate\""));
    }
}
