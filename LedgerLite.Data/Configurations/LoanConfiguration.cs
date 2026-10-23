using LedgerLite.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace LedgerLite.Data.Configurations;

public class LoanConfiguration : IEntityTypeConfiguration<Loan>
{
    public void Configure(EntityTypeBuilder<Loan> b)
    {
        b.ToTable("Loans");
        b.HasKey(x => x.Id);

        b.Property(x => x.CustomerName).HasMaxLength(80).IsRequired();
        b.Property(x => x.PrincipalAmount).HasPrecision(18, 2);
        b.Property(x => x.AnnualRatePercent).HasPrecision(5, 2);
        b.Property(x => x.DisbursementKey).HasMaxLength(100);

        b.HasIndex(x => x.DisbursementKey).IsUnique().HasFilter("[DisbursementKey] IS NOT NULL");

        // Restrict: two foreign keys to Accounts must not create multiple cascade paths
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.PrincipalAccountId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Account>().WithMany().HasForeignKey(x => x.InterestAccountId).OnDelete(DeleteBehavior.Restrict);
    }
}