using LedgerLite.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LedgerLite.Data.Configurations;

public class JournalEntryConfiguration : IEntityTypeConfiguration<JournalEntry>
{
    public void Configure(EntityTypeBuilder<JournalEntry> builder)
    {
        builder.ToTable("JournalEntries");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Date).IsRequired();
        builder.HasIndex(e => e.Date);

        builder.Property(e => e.PostedAt).HasColumnType("datetime2");
        builder.Property(e => e.Description).IsRequired().HasMaxLength(500);
        builder.Property(e => e.Reference).IsRequired().HasMaxLength(100);
        builder.Property(e => e.Source).HasConversion<string>().HasMaxLength(30);

        // Prevents double-posting by batch jobs: same key can only exist once
        builder.Property(e => e.IdempotencyKey).HasMaxLength(200);
        builder.HasIndex(e => e.IdempotencyKey)
            .IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL");

        // An entry can be reversed at most once
        builder.HasOne<JournalEntry>()
            .WithMany()
            .HasForeignKey(e => e.ReversalOfEntryId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.ReversalOfEntryId)
            .IsUnique()
            .HasFilter("[ReversalOfEntryId] IS NOT NULL");

        // Lines are stored in the private _lines field; no cascade because entries are never deleted
        builder.HasMany(e => e.Lines)
            .WithOne()
            .HasForeignKey(l => l.JournalEntryId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(e => e.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}