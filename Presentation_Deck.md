# NovaWallet Ledger Service — 10-Minute Presentation Deck

> **FirstBank Digital Factory — Backend / API Developer (.NET) Interview Presentation**  
> **Candidate Presentation & Technical Defense**  
> **Duration: 10 Minutes**

---

## Slide 1: Title Slide (0:00 - 1:00)
- **Title**: NovaWallet Ledger Service
- **Subtitle**: Financial-Grade, Concurrency-Safe Ledger for FirstBank NovaPay
- **Role**: Backend / API Developer (.NET) — FirstBank Digital Factory
- **Key Message**: Designing a high-throughput, zero-drift, deadlock-free financial ledger adhering to CBN regulations and FirstBank standards.

### Speaker Script (Slide 1)
> *"Good day members of the panel. Today, I am excited to present my design and implementation of the NovaWallet Ledger Service for FirstBank NovaPay.  
> As a core banking and wallet ledger, this service has one paramount responsibility: it must never lose, duplicate, or miscount a single kobo of our customers' money. Over the next 10 minutes, I will walk you through the Clean Architecture design, our strict concurrency and deadlock prevention strategies, the idempotency protocol, and how all hard constraints and stretch goals were achieved in .NET 9."*

---

## Slide 2: Problem Scenario & Nigerian Fintech Operating Context (1:00 - 2:15)
- **Product**: FirstBank NovaPay Super-App (NovaWallet, NovaSave, NovaLend, NovaBiz, Diaspora Remittance).
- **Hard Constraints & Real-World Constraints**:
  - **Kobo Standard**: Zero float/double anywhere in the money path.
  - **Interleaving Concurrency**: High-frequency concurrent requests must never allow negative balance or double spend.
  - **Operating Environment**: Tiered KYC (BVN/NIN), NIBSS NIP settlement simulation, USSD (*894#), NDPA 2023 compliance.
  - **West Africa Time (WAT)**: Daily ₦500k limit reset strictly at midnight WAT (UTC+1).

### Speaker Script (Slide 2)
> *"In the Nigerian financial landscape, high concurrency and low latency are daily realities. A user might initiate multiple quick transfers via our mobile app, USSD (*894#), or receive inbound NIBSS NIP transfers simultaneously.  
> To protect customer trust, we enforced non-negotiable financial invariants: all amounts are stored strictly as 64-bit integers in kobo to eliminate floating-point drift, balances can never drop below zero under any interleaved race condition, and daily limits are tied precisely to West Africa Time (UTC+1)."*

---

## Slide 3: Clean Architecture & System Design (2:15 - 3:45)
- **Clean Architecture Layers**:
  - **Domain**: Pure business entities (`Wallet`, `Transaction`, `AuditLog`, `IdempotencyRecord`, `OutboxMessage`), strict domain invariants, and custom domain exceptions.
  - **Application**: Use-cases (`TransferService`, `WalletService`, `StatementService`), DTOs, and FluentValidation.
  - **Infrastructure**: EF Core with PostgreSQL & SQLite, JWT Bearer handler, WAT Clock Provider, and Outbox background worker.
  - **API**: ASP.NET Core controllers, RFC 7807 Problem Details, Rate Limiting, and Swagger UI.
- **Visual Diagram**: Clean separation of dependencies pointing inward to Domain.

### Speaker Script (Slide 3)
> *"I structured the solution using Clean Architecture in .NET 9. The Domain layer is completely decoupled and contains our core business logic—such as Debit and Credit validation rules.  
> The Application layer orchestrates the financial use-cases with FluentValidation. Infrastructure handles our database, JWT authentication, and background workers. Finally, the API layer exposes clean RESTful endpoints protected by rate limiting and standardized RFC 7807 Problem Details error responses. This makes the code exceptionally readable, testable, and maintainable."*

---

## Slide 4: Concurrency Safety & Deadlock Elimination (3:45 - 5:15)
- **The Challenge**:
  - Double-Spend Race Condition: Two concurrent transfers depleting the same wallet.
  - Circular Wait Deadlock: Wallet A transfers to Wallet B while Wallet B transfers to Wallet A at the exact same millisecond.
- **The Solution**:
  1. **Deterministic Sorted Resource Acquisition**: Always acquire locks in order of `min(walletA, walletB)` then `max(walletA, walletB)`. This mathematically eliminates deadlocks.
  2. **ACID Transaction + Domain Debit Invariant**: `sourceWallet.Debit(amount)` checks `BalanceKobo >= amountKobo`.
  3. **Database-Level Defense in Depth**: `CHECK ("BalanceKobo" >= 0)` constraint applied directly on the database schema.

### Speaker Script (Slide 4)
> *"Concurrency safety was engineered with extreme care. When two users execute cross-transfers simultaneously—A sending to B and B sending to A—naive locking causes circular wait deadlocks.  
> We solved this through deterministic sorted resource acquisition: the system always locks the wallet with the smaller ID first, followed by the larger ID. This guarantees an acyclic lock hierarchy, making deadlocks impossible. Furthermore, with domain balance checks and database-level CHECK constraints, a wallet balance can never go negative under any load."*

---

## Slide 5: Financial Idempotency Protocol & Daily Limits (5:15 - 6:45)
- **Idempotency Header (`Idempotency-Key`)**:
  - Request Fingerprint: Cryptographic SHA-256 hash of `(sourceId, destId, amountKobo, reference)`.
  - **Identical Replay**: Returns the cached successful response without duplicate deductions.
  - **Payload Conflict**: Reusing a key with a different payload returns `422 Unprocessable Entity`.
  - **Atomic Persistence**: Idempotency records are saved in the exact same DB transaction as the financial mutations.
- **WAT Daily Limit**:
  - ₦500,000 (50,000,000 kobo) outbound cap per wallet.
  - Calculated dynamically using `IDateTimeProvider` against start-of-day in West Africa Time (`UTC+1`).

### Speaker Script (Slide 5)
> *"Network hiccups and mobile app retries often cause duplicate transfer requests. Our transfer endpoint requires or accepts an `Idempotency-Key` header.  
> We generate a SHA-256 fingerprint of the payload. If an identical request is replayed, the service returns the previous result without debiting the customer again. If a key is reused with a different amount or recipient, it is rejected with an RFC 7807 422 error.  
> Additionally, the daily limit of ₦500,000 is enforced server-side and resets dynamically at midnight West Africa Time, regardless of the server's UTC clock."*

---

## Slide 6: Audit Trail & Stretch Goals Delivered (6:45 - 8:00)
- **Immutable Append-Only Audit Trail**:
  - Separate `AuditLogs` table tracking `PreBalanceKobo`, `PostBalanceKobo`, `Delta`, `Operation`, `CorrelationId`, and `PerformedBy`.
- **4 Stretch Goals Implemented**:
  1. **Rate Limiting**: Sliding/fixed window limiter protecting transfer endpoints against DoS/abuse (`429 Too Many Requests`).
  2. **Transactional Outbox Pattern**: Publishes `TransferCompleted` domain events for asynchronous downstream services.
  3. **Structured Logging & Tracing**: `X-Correlation-Id` propagated across all log scopes and Problem Details.
  4. **Container Health Probes**: `/health/live` and `/health/ready` for container orchestrators.

### Speaker Script (Slide 6)
> *"For regulatory compliance and internal audit, every single balance mutation writes to an immutable append-only AuditLogs table recording the exact pre- and post-balances and trace IDs.  
> I also implemented all four stretch goals: token-bucket rate limiting on the transfer API, the Transactional Outbox pattern for publishing TransferCompleted domain events, structured logging with correlation IDs, and Kubernetes-ready health and readiness probes."*

---

## Slide 7: Automated Testing Rigor & Concurrency Verification (8:00 - 9:00)
- **Test Suite (13 / 13 Tests Passed)**:
  - **Multi-Threaded Concurrency Test**: 50 concurrent requests competing for ₦100 in funds. Exactly 20 transfers succeed, 30 fail with `InsufficientFundsException`, final balance is exactly 0, and total money is conserved.
  - **Deadlock Resistance Test**: 40 simultaneous cross-transfers execute cleanly.
  - **Idempotency Replay & Conflict Tests**: Verifies zero duplicate debits and mismatch rejection.
  - **WAT Daily Limit Boundary Tests**: Verifies limit blocking and midnight reset.
  - **End-to-End API Integration Tests**: Tests full HTTP request pipeline with JWT authentication and seeded data.

### Speaker Script (Slide 7)
> *"To prove our architecture under pressure, I wrote a comprehensive automated test suite in xUnit.  
> Most notably, our concurrency stress test launches 50 parallel threads against a single wallet. The results confirm that exactly the right number of transfers succeed, excess transfers are rejected cleanly, the final balance is exactly zero, and zero kobo is lost or created out of thin air. 100% of the test suite passes."*

---

## Slide 8: Deployment, Multi-Database Support & Conclusion (9:00 - 10:00)
- **Flexible Database Architecture**:
  - **Docker / Production**: Single-command startup (`docker compose up`) orchestrating the .NET 9 API and PostgreSQL 16 container.
  - **Local Development**: Frictionless zero-setup via SQLite, plus full native support for **Microsoft SQL Server** via EF Core.
  - **Pre-Seeded Demo Accounts**: `CUST-FIRSTBANK-001` (₦100,000) and `CUST-FIRSTBANK-002` (₦50,000) for instant Swagger testing.
  - **Interactive Swagger UI**: Reachable at `http://localhost:8080/swagger`.
- **Key Takeaways**:
  - 100% Kobo Integer Standard & Zero Float Drift.
  - Concurrency-safe, deadlock-free P2P transfers.
  - Stateful Idempotency and WAT midnight daily limits.
  - Append-only immutable audit trail and Transactional Outbox pattern.

### Speaker Script (Slide 8)
> *"For deployment, the entire service and its PostgreSQL datastore boot with a single command: `docker compose up`. For local development, it defaults to a zero-configuration SQLite database so anyone can clone and run it instantly, while fully supporting Microsoft SQL Server simply by updating the connection string in `appsettings.json`.  
> Pre-seeded demo accounts and full Swagger documentation are immediately ready for testing. In summary, this service delivers absolute financial correctness, high concurrency safety, complete auditability, and clean maintainable code. Thank you, and I welcome any questions or live code walk-throughs from the panel."*

---

## Live Code Walkthrough & Defense FAQ

### Q1: Why did you use sorted resource acquisition for concurrency instead of distributed Redis locks?
> **Answer**: *"For wallet-to-wallet transfers within a relational database, database row-level locking with sorted resource ordering (`min(idA, idB)` then `max(idA, idB)`) provides native ACID guarantees within the database transaction without introducing external network roundtrips or distributed lock expiration/split-brain risks. It is mathematically deadlock-free and extremely fast."*

### Q2: Why store monetary values as integer kobo instead of `decimal`?
> **Answer**: *"In .NET, `decimal` is 128-bit fixed-point, but `long` integer math in kobo represents the atomic smallest unit of the currency. Storing integer kobo completely eliminates any possibility of fractional rounding discrepancies across different database drivers, JSON serializers, or client platforms. 1 Naira is always 100 kobo."*

### Q3: How do you handle timezone transitions for the WAT daily limit?
> **Answer**: *"West Africa Time (WAT) has a fixed UTC+1 offset and does not observe Daylight Saving Time. We calculate the UTC boundary for WAT midnight (`00:00:00 WAT` = `23:00:00 UTC` previous day) using our `IDateTimeProvider`. This ensures every transaction's UTC timestamp is accurately aggregated against the Nigerian banking day."*

### Q4: Which databases does this service support and how is it configured?
> **Answer**: *"The service is provider-agnostic via Entity Framework Core. When running containerized via `docker compose up`, it connects to PostgreSQL 16. For local testing, it defaults to a zero-setup SQLite database file (`novawallet.db`), and it natively supports Microsoft SQL Server (LocalDB or SQL Server Express) simply by changing the `DefaultConnection` string in `appsettings.json`."*
