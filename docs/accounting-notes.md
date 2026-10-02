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

**Normal balance** is the side that increases the account. An account with a normal balance on the opposite side is unusual and worth investigating (for example, a Cash account with a credit balance).

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

| Term                         | Meaning                                                                                                      | In LedgerLite                 |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------ | ----------------------------- |
| Chart of accounts            | The list of all accounts the books can post to                                                               | `Account` table               |
| Account                      | A bucket that tracks one kind of value (cash, deposits owed, interest earned)                                | `Account`                     |
| Journal entry                | One financial event, recorded as a set of balanced movements                                                 | `JournalEntry`                |
| Journal line                 | One account affected by one event, on the debit or credit side                                               | `JournalLine`                 |
| Posting                      | Recording a journal entry in the ledger                                                                      | `PostJournalEntry` use case   |
| Ledger                       | All the lines for one account, in date order, with a running balance                                         | Account ledger view           |
| Trial balance                | Total debits and credits across all accounts; the two totals must match                                      | Trial balance report          |
| Reversal                     | A new entry that cancels an earlier one by flipping debits and credits                                       | `JournalEntry.CreateReversal` |
| Control account              | A parent account that rolls up many sub-accounts (e.g. Customer Deposits over each member's savings account) | `Account.ParentAccountId`     |
| Sub-ledger account           | A child account for one customer or product, under a control account                                         | Stage 2                       |
| Suspense account             | A holding account for money received but not yet allocated                                                   | Stage 2                       |
| Accrual                      | Recording income or expense when it is earned or owed, not when cash moves                                   | Stage 2 interest accrual      |
| Accounting date vs posted-at | The date the event belongs to vs the moment it was recorded                                                  | `Date` vs `PostedAt`          |
| Idempotency key              | A unique key that stops a retried job from posting the same entry twice                                      | `JournalEntry.IdempotencyKey` |

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
