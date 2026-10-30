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
        builder.Property(e => e.UnitsPerUsd).HasPrecision(18, 6);
        builder.Property(e => e.Source).HasMaxLength(50);

        builder.HasIndex(e => new { e.Currency, e.EffectiveDate }).IsUnique();
    }
}
