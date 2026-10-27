using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartLedger.Domain.Entities;
using SmartLedger.Infrastructure.Identity;

namespace SmartLedger.Infrastructure.Persistence.Configurations;

internal sealed class ReceiptConfiguration : IEntityTypeConfiguration<Receipt>
{
    public void Configure(EntityTypeBuilder<Receipt> builder)
    {
        builder.ToTable("Receipts");

        // Stored as text so the table is readable in SQL tools; adding a status never renumbers old rows.
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);

        builder.Property(r => r.VendorName).HasMaxLength(200);
        builder.Property(r => r.Currency).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        builder.Property(r => r.Subtotal).HasPrecision(18, 2);
        builder.Property(r => r.Tax).HasPrecision(18, 2);
        builder.Property(r => r.Total).HasPrecision(18, 2);
        builder.Property(r => r.PaymentMethod).HasMaxLength(60);
        builder.Property(r => r.Notes).HasMaxLength(500);
        builder.Property(r => r.FailureReason).HasMaxLength(300);

        builder.Property(r => r.FileName).HasMaxLength(255);
        builder.Property(r => r.ContentType).HasMaxLength(100).IsUnicode(false);
        builder.Property(r => r.StorageKey).HasMaxLength(500).IsUnicode(false);

        builder.Property(r => r.BaseCurrency).HasMaxLength(3).IsFixedLength().IsUnicode(false);
        builder.Property(r => r.ExchangeRate).HasPrecision(18, 8);
        builder.Property(r => r.BaseCurrencyTotal).HasPrecision(18, 2);

        builder.OwnsMany(r => r.ExtractionFlags, flags => flags.ToJson());

        builder.HasMany(r => r.LineItems)
            .WithOne()
            .HasForeignKey(l => l.ReceiptId)
            .OnDelete(DeleteBehavior.Cascade);

        // Categories are archived rather than deleted, so a category with receipts can't be removed.
        builder.HasOne<Category>()
            .WithMany()
            .HasForeignKey(r => r.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(r => r.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(r => new { r.UserId, r.Number }).IsUnique();
        builder.HasIndex(r => new { r.UserId, r.Status });
        builder.HasIndex(r => new { r.UserId, r.ReceiptDate });
    }
}
