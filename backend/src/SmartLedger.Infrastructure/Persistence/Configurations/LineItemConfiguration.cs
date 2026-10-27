using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartLedger.Domain.Entities;

namespace SmartLedger.Infrastructure.Persistence.Configurations;

internal sealed class LineItemConfiguration : IEntityTypeConfiguration<LineItem>
{
    public void Configure(EntityTypeBuilder<LineItem> builder)
    {
        builder.ToTable("LineItems");

        builder.Property(l => l.Description).HasMaxLength(200);
        builder.Property(l => l.Quantity).HasPrecision(10, 3);
        builder.Property(l => l.UnitPrice).HasPrecision(18, 2);
        builder.Property(l => l.Amount).HasPrecision(18, 2);

        builder.HasIndex(l => new { l.ReceiptId, l.LineNumber }).IsUnique();
    }
}
