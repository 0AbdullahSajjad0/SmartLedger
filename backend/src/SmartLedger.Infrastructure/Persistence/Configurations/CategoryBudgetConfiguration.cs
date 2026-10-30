using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartLedger.Domain.Entities;

namespace SmartLedger.Infrastructure.Persistence.Configurations;

internal sealed class CategoryBudgetConfiguration : IEntityTypeConfiguration<CategoryBudget>
{
    public void Configure(EntityTypeBuilder<CategoryBudget> builder)
    {
        builder.Property(b => b.Amount).HasPrecision(18, 2);

        // One entry per category and month; also the index for "latest entry on or before a month".
        builder.HasIndex(b => new { b.CategoryId, b.EffectiveFrom }).IsUnique();

        // A category's history goes with it (and with the user, through the category).
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(b => b.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
