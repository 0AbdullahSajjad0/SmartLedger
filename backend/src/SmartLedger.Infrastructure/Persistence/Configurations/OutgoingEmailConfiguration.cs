using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SmartLedger.Infrastructure.Email;
using SmartLedger.Infrastructure.Identity;

namespace SmartLedger.Infrastructure.Persistence.Configurations;

internal sealed class OutgoingEmailConfiguration : IEntityTypeConfiguration<OutgoingEmail>
{
    public void Configure(EntityTypeBuilder<OutgoingEmail> builder)
    {
        builder.ToTable("OutgoingEmails");

        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.LastError).HasMaxLength(500);

        // The worker's question: what is still waiting, and when is it due? Sent emails stay out of the index.
        builder.HasIndex(e => e.NextAttemptAt).HasFilter("[SentAt] IS NULL AND [GaveUpAt] IS NULL");

        // "Was a reset link asked for in the last few minutes?"
        builder.HasIndex(e => new { e.UserId, e.Kind, e.CreatedAt });

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
