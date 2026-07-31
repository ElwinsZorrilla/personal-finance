using Margen.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Margen.Infrastructure.Configurations;

internal sealed class CategoryBudgetConfiguration : IEntityTypeConfiguration<CategoryBudget>
{
    public void Configure(EntityTypeBuilder<CategoryBudget> builder)
    {
        builder.HasKey(b => b.Id);

        // Effective se calcula; no tiene columna. Guardarlo además de sus dos
        // sumandos crea un tercer sitio donde el mismo número puede estar mal.
        builder.Ignore(b => b.Effective);

        builder.HasOne(b => b.BudgetPeriod)
            .WithMany(p => p!.CategoryBudgets)
            .HasForeignKey(b => b.BudgetPeriodId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.Category)
            .WithMany(c => c!.Budgets)
            .HasForeignKey(b => b.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        // Una categoría tiene una sola línea por período.
        builder.HasIndex(b => new { b.BudgetPeriodId, b.CategoryId }).IsUnique();
    }
}
