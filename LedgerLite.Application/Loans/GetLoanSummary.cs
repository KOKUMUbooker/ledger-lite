using LedgerLite.Application.Loans;
using LedgerLite.Data;

namespace LedgerLite.Application.Loans;

public class GetLoanSummary
{
    private readonly LedgerDbContext _db;
    public GetLoanSummary(LedgerDbContext db) => _db = db;

    public async Task<LoanSummary> ExecuteAsync(int loanId, CancellationToken ct = default)
    {
        var loan = await LoanRules.RequireAsync(_db, loanId, ct);
        return (await LoanRules.SummariesAsync(_db, new[] { loan }, ct))[0];
    }
}