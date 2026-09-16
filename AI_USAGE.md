# AI Usage Documentation (`AI_USAGE.md`)

> **FirstBank Digital Factory — Take-Home Assessment**  
> **Candidate Evaluation: AI Fluency, Judgment & Financial Safety Engineering**

---

## 1. AI Tools Used and Purpose

| Tool | Primary Purpose | How It Was Directed |
|---|---|---|
| **Claude 3.7 Sonnet / DeepMind Antigravity** | Architecture scaffolding, Clean Architecture boilerplate generation, test case synthesis. | Directed with strict domain constraints (e.g. prohibition of floating-point numbers, deadlock prevention, RFC 7807 compliance). |
| **GitHub Copilot / IntelliCode** | In-line auto-completion for repetitive LINQ queries, DTO property mappings, and test assertion scaffolding. | Used as a typing accelerator while retaining manual architectural control. |

---

## 2. Concrete Prompts & Outputs

### Prompt 1: High-Concurrency Double-Spend Prevention & Deadlock Handling
* **Prompt Given**:  
  > *"Write an atomic transfer method in C# .NET 9 between two wallets using Entity Framework Core. Ensure it handles concurrent requests safely without allowing negative balances or deadlocks when Wallet A sends to Wallet B while Wallet B simultaneously sends to Wallet A."*
* **AI Output Returned**:  
  The AI suggested wrapping the two `FindAsync` calls in an EF Core transaction:
  ```csharp
  var source = await dbContext.Wallets.FindAsync(sourceId);
  var dest = await dbContext.Wallets.FindAsync(destId);
  source.Balance -= amount;
  dest.Balance += amount;
  await dbContext.SaveChangesAsync();
  ```
* **Why AI's Initial Output Was Naive / Unsafe**:
  1. **Deadlock Hazard**: If Thread 1 executes transfer $A \rightarrow B$ and Thread 2 executes transfer $B \rightarrow A$ concurrently, Thread 1 acquires a lock on $A$ and waits for $B$, while Thread 2 acquires a lock on $B$ and waits for $A$, creating a classic circular wait database deadlock.
  2. **Race Condition / Lost Update**: Standard `FindAsync` without explicit locking or optimistic concurrency checks allows interleaved reads where both threads see initial balance $10,000$ kobo and both approve two $10,000$ kobo debits, causing balance to drop to $-10,000$ kobo (double-spend).
* **How I Caught & Fixed It**:
  1. **Sorted Lock Ordering**: Implemented deterministic resource acquisition where wallets are always queried in sorted `Guid` order (`min(sourceId, destId)` followed by `max(sourceId, destId)`). This breaks the circular wait condition and guarantees deadlock-free transfers.
  2. **Domain-Enforced Non-Negative Debit**: Added invariant validation in the `Wallet.Debit` domain method and a database-level constraint `CHECK (BalanceKobo >= 0)`.

---

### Prompt 2: Financial Idempotency Protocol
* **Prompt Given**:  
  > *"Implement idempotency handling in ASP.NET Core for an HTTP POST /api/transfers endpoint accepting an `Idempotency-Key` header."*
* **AI Output Returned**:  
  The AI proposed a controller-level dictionary or cache lookup:
  ```csharp
  if (memoryCache.TryGetValue(idempotencyKey, out var result)) return Ok(result);
  var transfer = await ExecuteTransfer();
  memoryCache.Set(idempotencyKey, transfer);
  return Ok(transfer);
  ```
* **Why AI's Initial Output Was Naive / Unsafe**:
  1. **Volatile In-Memory Storage**: An in-memory cache loses state across pod restarts and multi-instance container horizontal scaling in Kubernetes/Docker.
  2. **Payload Fingerprint Omission**: Reusing the same `Idempotency-Key` with a *different* destination or higher amount would silently return the previous cached response without validating the payload integrity.
  3. **Non-Atomic Persistence**: If the server crashed after transferring funds but before caching the key, a retry would double-debit the customer.
* **How I Caught & Fixed It**:
  1. **SHA-256 Request Fingerprinting**: Computed a deterministic SHA-256 hash of `(sourceId, destId, amountKobo, reference)`. If the key exists with an identical hash, the cached result is returned; if the hash differs, the service rejects the request with an RFC 7807 `422 Unprocessable Entity` problem detail.
  2. **Transactional Persistence**: The idempotency record is persisted inside the *same ACID database transaction* as the wallet balance mutations and ledger records.

---

### Prompt 3: Daily Limit Reset at Midnight WAT
* **Prompt Given**:  
  > *"Write a daily outbound transfer limit checker in .NET that resets every day at midnight in Nigerian local time (WAT)."*
* **AI Output Returned**:  
  The AI wrote:
  ```csharp
  var today = DateTime.Today; // local server midnight
  var spent = dbContext.Transactions.Where(t => t.CreatedAt >= today).Sum(t => t.Amount);
  ```
* **Why AI's Initial Output Was Naive / Unsafe**:
  1. `DateTime.Today` relies on the host container's local clock (which defaults to UTC in standard Docker/Linux environments). This causes the daily limit to reset at 01:00 AM WAT instead of 00:00 AM WAT (a 1-hour timezone drift).
  2. Floating-point `decimal`/`double` types were used instead of explicit 64-bit integer kobo arithmetic.
* **How I Caught & Fixed It**:
  1. Built an explicit `IDateTimeProvider` with timezone awareness supporting both Windows (`W. Central Africa Standard Time`) and Linux/IANA (`Africa/Lagos`) timezone databases.
  2. Calculated exact UTC start-of-day boundary for midnight WAT (`GetWatMidnightTodayUtc()`) and performed indexed integer aggregation `SumAsync(t => (long?)t.AmountKobo)`.

---

## 3. Summary of Engineering Judgment

AI tools were leveraged to accelerate boilerplate creation and explore test permutations. However, **core financial invariants, concurrency correctness, deadlock prevention, timezone boundary precision, and idempotency guarantees** were strictly scrutinized, verified, and hardened through rigorous automated stress testing.
