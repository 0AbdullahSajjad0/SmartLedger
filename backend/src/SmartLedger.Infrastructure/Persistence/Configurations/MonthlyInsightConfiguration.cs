using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartLedger.Domain.Entities;
using SmartLedger.Infrastructure.Identity;

namespace SmartLedger.Infrastructure.Persistence.Configurations;

internal sealed class MonthlyInsightConfiguration : IEntityTypeConfiguration<MonthlyInsight>
{
    public void Configure(EntityTypeBuilder<MonthlyInsight> builder)
    {
        builder.ToTable("MonthlyInsights");

        builder.Property(i => i.Headline).HasMaxLength(300);
        builder.Property(i => i.Model).HasMaxLength(100);

        builder.HasIndex(i => new { i.UserId, i.Year, i.Month }).IsUnique();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(i => i.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
