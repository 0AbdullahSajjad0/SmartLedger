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
        // Up to 12 digits before the point (the Iranian rial is ~42,000 per dollar), 6 after (the Kuwaiti dinar is ~0.307).
        builder.Property(e => e.UnitsPerUsd).HasPrecision(18, 6);
        builder.Property(e => e.Source).HasMaxLength(50);

        // One rate per currency per day. Lookups are by (currency, date), and the index rejects a duplicate when two
        // approvals fetch the same day at once.
        builder.HasIndex(e => new { e.Currency, e.EffectiveDate }).IsUnique();
    }
}
