using LedgerLite.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LedgerLite.Data.Configurations;

public class JournalLineConfiguration : IEntityTypeConfiguration<JournalLine>
{
    public void Configure(EntityTypeBuilder<JournalLine> builder)
    {
        builder.ToTable("JournalLines", t => t.HasCheckConstraint(
            "CK_JournalLines_DebitCredit",
            "[Debit] >= 0 AND [Credit] >= 0 AND ([Debit] = 0 OR [Credit] = 0) AND ([Debit] + [Credit]) > 0"));

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Debit).HasPrecision(18, 2);
        builder.Property(l => l.Credit).HasPrecision(18, 2);

        // Every ledger view and balance query filters by account
        builder.HasIndex(l => l.AccountId);

        // An account with history can never be deleted
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(l => l.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}