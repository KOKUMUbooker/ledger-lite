using LedgerLite.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LedgerLite.Data.Configurations;

public class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("Accounts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Code).IsRequired().HasMaxLength(20);
        builder.HasIndex(a => a.Code).IsUnique();

        builder.Property(a => a.Name).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Type).HasConversion<string>().HasMaxLength(20);

        // Parent/child hierarchy: can't delete a parent that still has children
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(a => a.ParentAccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Starter chart of accounts (anonymous types are how EF seeds private-setter entities)
        builder.HasData(
            new { Id = 1, Code = "1000", Name = "Cash", Type = AccountType.Asset, ParentAccountId = (int?)null, IsActive = true },
            new { Id = 2, Code = "1200", Name = "Loan Receivable", Type = AccountType.Asset, ParentAccountId = (int?)null, IsActive = true },
            new { Id = 3, Code = "2000", Name = "Customer Deposits", Type = AccountType.Liability, ParentAccountId = (int?)null, IsActive = true },
            new { Id = 4, Code = "3000", Name = "Share Capital", Type = AccountType.Equity, ParentAccountId = (int?)null, IsActive = true },
            new { Id = 5, Code = "4000", Name = "Interest Income", Type = AccountType.Income, ParentAccountId = (int?)null, IsActive = true },
            new { Id = 6, Code = "5000", Name = "Interest Expense", Type = AccountType.Expense, ParentAccountId = (int?)null, IsActive = true }
        );
    }
}