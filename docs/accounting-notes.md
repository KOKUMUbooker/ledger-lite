# Accounting Notes

Reference for the accounting concepts used in LedgerLite. If a debit/credit question comes up while coding, check here first.

## 1. The one idea everything follows from

```
Assets = Liabilities + Equity
```

- The **left side** of the equation (Assets) increases with a **debit**.
- The **right side** (Liabilities and Equity) increases with a **credit**.
- **Income** increases equity, so it behaves like equity: credit increases it.
- **Expenses** reduce equity, so they behave like assets (the opposite of equity): debit increases them.

Debit and credit are not "money in" and "money out". They are the left and right side of an entry. Whether a side means increase or decrease depends on the account type.

## 2. Debit / credit table

| Account type | Examples (LedgerLite) | Debit    | Credit   | Normal balance |
| ------------ | --------------------- | -------- | -------- | -------------- |
| Asset        | Cash, Loan Receivable | Increase | Decrease | Debit          |
| Liability    | Customer Deposits     | Decrease | Increase | Credit         |
| Equity       | Share Capital         | Decrease | Increase | Credit         |
| Income       | Interest Income       | Decrease | Increase | Credit         |
| Expense      | Interest Expense      | Increase | Decrease | Debit          |

**Normal balance** is the side on which an account's balance is normally positive, which is the same side that increases it. An account whose balance sits on the opposite side is unusual and worth investigating.

Example: Cash is debit-normal. If debits total 5,000 and credits total 2,000, Cash has a debit balance of 3,000, which is healthy. If credits ever exceeded debits, Cash would have a credit balance, meaning more cash was paid out than ever came in, so something is wrong. Customer Deposits is the mirror image: a credit balance is normal (the bank owes depositors), and a debit balance would be unusual.

Normal balance describes an account's usual state. It is not a rule each transaction must follow, and an account can briefly go abnormal (for example, a customer overpays a loan, so Loan Receivable has a credit balance). The system allows it and it is flagged for investigation.

It matters because it picks the balance formula (section 3), acts as a quick sanity check, and decides which trial balance column an account's balance appears in.

### Memory aid

```
Debit increases:   Assets, Expenses
Credit increases:  Liabilities, Income, Equity       (DEA-LIE)
```

## 3. Balance formula (the sign flip)

Balances are never stored. They are always derived from journal lines:

| Account type                              | Balance =                    |
| ----------------------------------------- | ---------------------------- |
| Asset, Expense (debit-normal)             | Total Debits - Total Credits |
| Liability, Equity, Income (credit-normal) | Total Credits - Total Debits |

In code: `Account.BalanceFrom(totalDebits, totalCredits)`.

## 4. Key terms

| Term                         | Meaning                                                                                                                                                                                                       | In LedgerLite                                                 |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------- |
| Chart of accounts            | The list of all accounts the books can post to                                                                                                                                                                | `Account` table                                               |
| Account                      | A bucket that tracks one kind of value (cash, deposits owed, interest earned)                                                                                                                                 | `Account`                                                     |
| Journal entry                | One financial event (the whole transaction), made up of two or more lines that balance                                                                                                                        | `JournalEntry`                                                |
| Journal line                 | One account's part of a journal entry: an amount on the debit or credit side of a single account. Like a line item on a receipt, it only has meaning as part of its entry                                     | `JournalLine`                                                 |
| Posting                      | Validating a journal entry and saving it permanently, so it counts in every account it touches                                                                                                                | `PostJournalEntry` use case                                   |
| Posting rules                | The logic that decides _which lines_ a business transaction produces (e.g. a cash deposit = debit Cash, credit the customer's account)                                                                        | Stage 2 use cases such as `DepositCash`; see section 10       |
| Ledger                       | The permanent record of every movement, viewed by account: one account's lines in date order with a running balance. The "general ledger" is all accounts together. Stored once, as journal entries and lines | `JournalEntries` + `JournalLines` tables; account ledger view |
| Normal balance               | The side (debit or credit) on which an account's balance is normally positive; the same side that increases it                                                                                                | `AccountType.IsDebitNormal()`                                 |
| Trial balance                | Total debits and credits across all accounts; the two totals must match                                                                                                                                       | Trial balance report                                          |
| Reversal                     | A new entry that cancels an earlier one by flipping debits and credits                                                                                                                                        | `JournalEntry.CreateReversal`                                 |
| Control account              | A parent account that rolls up many sub-accounts (e.g. Customer Deposits over each member's savings account)                                                                                                  | `Account.ParentAccountId`                                     |
| Sub-ledger account           | A child account for one customer or product, under a control account                                                                                                                                          | Stage 2                                                       |
| Suspense account             | A holding account for money received but not yet allocated                                                                                                                                                    | Stage 2                                                       |
| Accrual                      | Recording income or expense when it is earned or owed, not when cash moves                                                                                                                                    | Stage 2 interest accrual                                      |
| Accounting date vs posted-at | The date the event belongs to vs the moment it was recorded                                                                                                                                                   | `Date` vs `PostedAt`                                          |
| Idempotency key              | A unique key that stops a retried job from posting the same entry twice                                                                                                                                       | `JournalEntry.IdempotencyKey`                                 |

## 5. Rules LedgerLite enforces

1. Every entry has at least two lines.
2. Total debits equal total credits.
3. Each line has either a debit or a credit, never both, never zero.
4. Amounts are never negative and have at most 2 decimal places.
5. Posted entries are never edited or deleted. Corrections are made with a reversal.
6. An entry can be reversed at most once.
7. An account with posted lines cannot be deleted (deactivate it instead).
8. Balances are derived from lines, never stored on the account.

## 6. Whose books are these?

The whole ledger belongs to the **bank (or SACCO)**. There is no separate customer ledger. A customer's savings account is a liability in the bank's books: money the bank owes back to the customer.

| Real-world fact                  | Bank's view                                                             |
| -------------------------------- | ----------------------------------------------------------------------- |
| Customer deposits cash           | Bank has more cash (asset up) and owes the customer more (liability up) |
| Customer takes a loan            | Customer owes the bank (asset up: Loan Receivable)                      |
| Customer pays interest on a loan | Bank earns income (income up)                                           |
| Bank pays interest on savings    | Bank incurs an expense and owes the customer more (liability up)        |

## 7. Worked examples

Each example is **one journal entry** with two or more lines.

### 7.1 Customer deposits 5,000 cash

| Account                       | Debit | Credit | Effect       |
| ----------------------------- | ----- | ------ | ------------ |
| Cash (asset)                  | 5,000 |        | Asset up     |
| Customer Deposits (liability) |       | 5,000  | Liability up |

Cash arrived, and the bank now owes the customer 5,000.

### 7.2 Customer withdraws 2,000 cash

| Account                       | Debit | Credit | Effect         |
| ----------------------------- | ----- | ------ | -------------- |
| Customer Deposits (liability) | 2,000 |        | Liability down |
| Cash (asset)                  |       | 2,000  | Asset down     |

### 7.3 Loan of 10,000 disbursed in cash

| Account                 | Debit  | Credit | Effect     |
| ----------------------- | ------ | ------ | ---------- |
| Loan Receivable (asset) | 10,000 |        | Asset up   |
| Cash (asset)            |        | 10,000 | Asset down |

No income is recorded. Cash turned into a different asset: a promise to be repaid. If the loan is paid into the customer's savings account instead, credit Customer Deposits in place of Cash.

### 7.4 Loan repayment of 1,100 (1,000 principal + 100 interest)

| Account                  | Debit | Credit | Effect                        |
| ------------------------ | ----- | ------ | ----------------------------- |
| Cash (asset)             | 1,100 |        | Asset up                      |
| Loan Receivable (asset)  |       | 1,000  | Asset down (principal repaid) |
| Interest Income (income) |       | 100    | Income up (interest earned)   |

An entry can have more than two lines. Total debits (1,100) still equal total credits (1,000 + 100).

### 7.5 Interest of 50 credited to a savings account

| Account                       | Debit | Credit | Effect       |
| ----------------------------- | ----- | ------ | ------------ |
| Interest Expense (expense)    | 50    |        | Expense up   |
| Customer Deposits (liability) |       | 50     | Liability up |

### 7.6 Member buys shares worth 3,000

| Account                | Debit | Credit | Effect    |
| ---------------------- | ----- | ------ | --------- |
| Cash (asset)           | 3,000 |        | Asset up  |
| Share Capital (equity) |       | 3,000  | Equity up |

### 7.7 Reversal of entry #4471 (the 5,000 deposit)

Original entry #4471:

| Account           | Debit | Credit |
| ----------------- | ----- | ------ |
| Cash              | 5,000 |        |
| Customer Deposits |       | 5,000  |

Reversal entry #4472 (`ReversalOfEntryId = 4471`):

| Account           | Debit | Credit |
| ----------------- | ----- | ------ |
| Cash              |       | 5,000  |
| Customer Deposits | 5,000 |        |

Both entries stay in the ledger. Their effects cancel out, and the audit trail is preserved. A reversal is always a **separate entry**, never extra lines on the original.

## 8. Starter chart of accounts (seeded)

| Code | Name              | Type      |
| ---- | ----------------- | --------- |
| 1000 | Cash              | Asset     |
| 1200 | Loan Receivable   | Asset     |
| 2000 | Customer Deposits | Liability |
| 3000 | Share Capital     | Equity    |
| 4000 | Interest Income   | Income    |
| 5000 | Interest Expense  | Expense   |

## 9. Checklist for modelling any transaction

1. What happened in the real world?
2. Which accounts changed, and what type is each?
3. Did each one go up or down?
4. Using the table in section 2, is each change a debit or a credit?
5. Do total debits equal total credits? If not, a side is missing.

## 10. Posting vs posting rules

These are two different layers, and it is easy to mix them up.

|                                 | Posting                                                                                    | Posting rules                                                                    |
| ------------------------------- | ------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------- |
| Question it answers             | "Is this set of lines valid, and can it be saved safely?"                                  | "Which lines does this business transaction produce?"                            |
| Where it lives                  | `PostJournalEntry` (Stage 1, built)                                                        | One small class per transaction type (Stage 2, planned)                          |
| Example                         | Reject an unbalanced entry; reject an inactive account; refuse a duplicate idempotency key | A cash deposit of X means: debit Cash X, credit the customer's deposit account X |
| Knows about deposits and loans? | No. It only sees accounts, debits, and credits                                             | Yes                                                                              |

The worked examples in section 7 are posting rules. Each one is a recipe for turning a business event into lines. `PostJournalEntry` takes the lines that a rule produced and records them safely.

```
Stage 2 business operation              Stage 1 ledger engine
(decides WHAT the lines are)       →    (records them safely)

DepositCash(5000)     ─┐
WithdrawCash(2000)    ─┤
DisburseLoan(10000)   ─┼──→ builds lines ──→ PostJournalEntry ──→ saved
RepayLoan(1100)       ─┤
BuyShares(3000)       ─┘
```

Example of a posting rule in code (Stage 2):

```csharp
public class DepositCash
{
    private readonly PostJournalEntry _post;
    public DepositCash(PostJournalEntry post) => _post = post;

    public Task<PostJournalEntryResult> ExecuteAsync(int customerAccountId, decimal amount, string reference)
    {
        // Posting rule for a cash deposit: Dr Cash, Cr the customer's deposit account
        var lines = new[]
        {
            new PostLine(CashAccountId, Debit: amount, Credit: 0),
            new PostLine(customerAccountId, Debit: 0, Credit: amount)
        };
        return _post.ExecuteAsync(new PostJournalEntryCommand(Today, "Cash deposit", reference, lines));
    }
}
```

Why keep them separate:

- Balance checks, account checks, and idempotency are written once, in `PostJournalEntry`, and every transaction type gets them for free.
- A new transaction type is a new small class, with no change to the ledger engine.
- Stage 1 exposes the raw "post these lines" operation on purpose, so you can type debits and credits yourself and see why they must balance.

Loan repayment (7.4) is not a plain if-condition. Its posting rule also needs the principal and interest split from the loan schedule, which is the interesting part of Stage 2.

## Posting rules: savings and loans

Every product operation is a balanced journal entry built by a posting rule. The rules never touch balances
directly; balances are derived from the lines.

| Operation                               | Debit                            | Credit                                     | Guard                                |
| --------------------------------------- | -------------------------------- | ------------------------------------------ | ------------------------------------ |
| Customer deposits cash                  | Cash                             | Customer savings (liability)               | Target must be a savings sub-account |
| Customer withdraws cash                 | Customer savings                 | Cash                                       | Balance must cover the amount        |
| Loan disbursed                          | Loan principal (asset)           | Cash                                       | Valid amount, rate and term          |
| Month's interest accrues                | Loan interest receivable (asset) | Interest income                            | One per loan per month               |
| Customer repays 150 (100 interest owed) | Cash 150                         | Interest receivable 100, Loan principal 50 | Cannot exceed what is owed           |

**Why savings is a liability.** The cash in the till is the bank's asset, but the customer can claim it back.
A deposit raises both Cash (asset) and what we owe the customer (liability), so the equation stays balanced.

**Why interest is accrued.** Interest is _earned_ as time passes, not when it is paid. Accrual debits an
interest receivable (what the customer owes us) and credits income straight away; the repayment later moves
money from receivable to cash. Income is therefore recognised in the right month.

**Repayment order.** Interest is paid before principal. Changing that policy is one line in `RepayLoan`.

**Rounding.** Monthly interest is `principal x annual rate / 1200`, rounded once to cents with half-away-from-zero
(0.005 becomes 0.01). The rounded figure is what is posted, so the books always hold exact cents.
