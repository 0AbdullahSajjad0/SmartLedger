using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartLedger.Domain.Entities;
using SmartLedger.Infrastructure.Identity;

namespace SmartLedger.Infrastructure.Persistence.Configurations;

internal sealed class BudgetAlertConfiguration : IEntityTypeConfiguration<BudgetAlert>
{
    public void Configure(EntityTypeBuilder<BudgetAlert> builder)
    {
        builder.Property(a => a.Level).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Spent).HasPrecision(18, 2);
        builder.Property(a => a.Budget).HasPrecision(18, 2);

        // Each level is sent once per category and month, even when two approvals race.
        builder.HasIndex(a => new { a.CategoryId, a.Year, a.Month, a.Level }).IsUnique();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Not cascading: the user's deletion already removes both (SQL Server allows only one cascade path).
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(a => a.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
