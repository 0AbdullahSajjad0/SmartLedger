using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartLedger.Domain.Entities;

namespace SmartLedger.Infrastructure.Persistence.Configurations;

internal sealed class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.ToTable("ExchangeRates");

        builder.Property(e => e.Currency).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        builder.Property(e => e.RateToUsd).HasPrecision(18, 8);
        builder.Property(e => e.Source).HasMaxLength(50);

        // One rate per currency per day; lookups take the latest row on or before the receipt date.
        builder.HasIndex(e => new { e.Currency, e.EffectiveDate }).IsUnique();
    }
}
