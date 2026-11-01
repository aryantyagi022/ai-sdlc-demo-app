# Research: Approval Limits by Role

No NEEDS CLARIFICATION remained in the Technical Context. These are the design questions the codebase and decisions raised.

## R-1 Where do the approval rules live?

- **Decision**: In a pure `ApprovalPolicy`; `ApprovalService` only orchestrates.
- **Rationale**: Pure functions of `(Invoice, Approver)` are testable without a repository, `EligibilityFor` can reuse
  them without a try/catch round-trip, and the mutation run targets one small class.
- **Alternatives**: Keep rules in the service (current; mixes rules with lookup/save, so order is harder to pin).

## R-2 How is a refusal distinguished from a conflict?

- **Decision**: Keep `ApprovalRefusedException` (separate from `InvalidOperationException`) → 403 with `{ error, reason }`.
- **Rationale**: `InvalidOperationException` already means 409 (lifecycle, stale write). Reusing it would merge two
  different statuses. Already implemented and mapped in `ApiControllerBase`.
- **Alternatives**: A status property on one exception type (more branching in the controller base).

## R-3 Validation order on Reject

- **Decision**: role → lookup → lifecycle → reason → decide.
- **Rationale**: Confirmed decision. The current code checks the reason before lookup/lifecycle, so an unauthorized or
  non-pending case could be reported as 400. Moving the check fixes that.
- **Alternatives**: Leave as is (violates the decision).

## R-4 Concurrency

- **Decision**: Compare-and-swap through the existing `Update` (`Version` check under a lock); `false` becomes 409.
- **Rationale**: Already the repository contract; exactly one writer can match the version. No new locking needed. Which
  request wins is deliberately not specified.
- **Alternatives**: A service-level lock per invoice (does not survive a second app instance, duplicates the repository's job).

## R-5 A missing reason must not beat the authority check

- **Finding**: `RejectRequest(string Reason)` with `Nullable` enabled and `[ApiController]` gets implicit `[Required]`
  model validation, which returns 400 *before* the action runs. An Admin sending no reason would see 400, not 403,
  and would learn nothing about authority — contradicting "RoleCannotApprove before reason validation".
- **Decision**: `RejectRequest(string? Reason)` and an optional body; the service owns the 400.
- **Alternatives**: Disable implicit required globally (broader than needed).

## R-6 Refusal log

- **Decision**: `IRefusalLog` in Domain, in-memory implementation in Api, recorded by the service for the five coded
  refusals only. Lifecycle (409), not-found (404) and bad-argument (400) outcomes have no refusal code and are not logged.
- **Rationale**: FR-016 requires a refusal code per entry. Keeps the invoice and repository untouched.
- **Alternatives**: Log through `ILogger` (not queryable by tests, not a structured record).

## R-7 `UpdateCount` meaning

- **Decision**: Count successful saves in `FakeInvoiceRepository`.
- **Rationale**: "Nothing was saved" is the assertion. A failed compare-and-swap does not save, so it does not count.
