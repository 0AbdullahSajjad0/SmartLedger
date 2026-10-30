using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartLedger.Domain.Entities;

namespace SmartLedger.Infrastructure.Persistence.Configurations;

internal sealed class CategoryBudgetConfiguration : IEntityTypeConfiguration<CategoryBudget>
{
    public void Configure(EntityTypeBuilder<CategoryBudget> builder)
    {
        builder.Property(b => b.Amount).HasPrecision(18, 2);

        builder.HasIndex(b => new { b.CategoryId, b.EffectiveFrom }).IsUnique();

        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(b => b.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
