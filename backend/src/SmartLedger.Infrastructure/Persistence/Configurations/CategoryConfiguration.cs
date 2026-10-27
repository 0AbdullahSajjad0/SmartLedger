using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartLedger.Domain.Entities;
using SmartLedger.Infrastructure.Identity;

namespace SmartLedger.Infrastructure.Persistence.Configurations;

internal sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");

        builder.Property(c => c.Name).HasMaxLength(60);
        builder.Property(c => c.Color).HasMaxLength(7).IsFixedLength().IsUnicode(false);
        builder.Property(c => c.MonthlyBudget).HasPrecision(18, 2);

        builder.HasIndex(c => new { c.UserId, c.Name }).IsUnique();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
