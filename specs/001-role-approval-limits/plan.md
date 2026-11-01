# Implementation Plan: Approval Limits by Role

**Branch**: `001-role-approval-limits` | **Date**: 2026-10-31 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/001-role-approval-limits/spec.md`

## Summary

Most of the ladder already exists (`ApprovalPolicy`, `ApprovalService`, `ApprovalRefusedException` → 403, version-checked
repository). The plan is therefore a **delta**: pull the decision rules into a *pure* `ApprovalPolicy`, fix the
reject-path validation order, add a separate refusal log, make the concurrency guarantee provable, and add the tests
that prove each rule. No new endpoints, no UC-1 (bulk) or UC-2 (partial) behaviour.

## Technical Context

**Language/Version**: C# / .NET 8 (existing layering: Domain, Api, Tests)

**Primary Dependencies**: ASP.NET Core 8 Web API, xUnit; Stryker 4.13.0 for mutation; no new packages

**Storage**: In-memory (`InMemoryInvoiceRepository`); refusal log is a second in-memory store

**Testing**: xUnit with `FakeInvoiceRepository` (gains `UpdateCount`); `WebApplicationFactory` for controller tests

**Target Platform**: Linux server (demo app)

**Project Type**: web-service (API + static UI)

**Performance Goals**: N/A (demo scale)

**Constraints**: Domain has no web or storage code; refusal maps to 403 via its own exception because 409 is already
used by `InvalidOperationException` (lifecycle and stale-write conflicts)

**Scale/Scope**: 4 demo users, 9 seeded invoices

## Constitution Check

| Principle | Status | Note |
|---|---|---|
| I. Rules live only in Domain | Pass | All checks move into `ApprovalPolicy`; controller keeps none |
| II. Positive and negative test per rule | Pass (planned) | Boundary pairs at 50,000 / 500,000; each proved by breaking the rule |
| III. A refusal changes nothing | Pass (planned) | `UpdateCount == 0` and unchanged `Version` asserted for every refusal |
| IV. Controllers stay thin | Pass | Only change: `RejectRequest.Reason` becomes nullable (see R-5) |
| V. Commits are small | Pass (planned) | One commit per task |
| VI. Mutation score never drops | Gate | Re-run Stryker after the policy move; compare to `docs/mutation/before.txt` |
| VII. No real credentials | Pass | None touched |

**Doc drift to fix** (not a violation): `AGENTS.md` says "Approval rules live in `ApprovalService`". After this
change the rules live in `ApprovalPolicy` and the service only orchestrates. Update that sentence in the same change.

Re-check after design: still passing; no Complexity Tracking entries.

## Decided Order of Evaluation

Approve and Reject share steps 1–3; this is the contract the tests pin.

1. `RoleCannotApprove` — role not in `Approval:RoleLimits` → 403. **Before invoice lookup and before reason validation.**
2. Invoice not found → 404.
3. Not `PendingApproval` → 409 (`InvalidOperationException`, no reason code).
4. Reject only: blank reason → 400. Then decide; nothing further to check (submitter may reject).
5. Approve only: `SelfApprovalNotAllowed` → `DuplicateApprover` → (above threshold: `SecondApproverMustBeDirector`; else:
   `AboveApprovalLimit`) → 403.
6. Save through `IInvoiceRepository.Update` (compare-and-swap on `Version`); `false` → 409.

Existing gap fixed: today `Reject` validates the reason (step 4) *before* lookup and lifecycle.

## Design

### Domain (`backend/src/InvoiceApp.Domain`)

- `ApprovalPolicy` gains two pure methods (no I/O, no clock, no exceptions, no logging):
  - `ApprovalRefusal? RefuseRole(Approver)` — step 1.
  - `ApprovalRefusal? RefuseApproval(Invoice, Approver)` — step 5, with the message text moved from the service.
- `ApprovalRefusal` is a new record `(ApprovalRefusalReason Reason, string Message)` in `ApprovalRefusal.cs`.
- `ApprovalService` orchestrates only: calls the policy, throws `ApprovalRefusedException` on a refusal, handles lookup,
  lifecycle, reason, save. `EligibilityFor` calls the same policy methods in the same order (no try/catch round-trip).
- New `IRefusalLog` (Domain interface) with `void Record(RefusalLogEntry)`; the service records an entry for each of the
  five coded refusals, then rethrows. Logging is separate from the invoice and never touches the repository.
- New `RefusalLogEntry(InvoiceId, Username, Role, Action, Reason, Message, OccurredAt)`; `Action` is an enum
  `Approve | Reject`.
- `ApprovalRefusedException` is unchanged and stays separate from `InvalidOperationException`.

### Api (`backend/src/InvoiceApp.Api`)

- `InMemoryRefusalLog : IRefusalLog` (thread-safe list); registered as a singleton in `Program.cs` (wiring only).
- `ApprovalsController`: `RejectRequest(string? Reason)` and `[FromBody] RejectRequest? request` so a missing body or
  property reaches the service. See R-5.
- No change to `ApiControllerBase`: 404 / 403 / 409 / 400 mapping already matches FR-013.

### Tests (`backend/tests/InvoiceApp.Tests`)

- `FakeInvoiceRepository.UpdateCount`: number of **successful** saves. Every refusal test asserts it is 0 and re-reads
  the invoice to compare `Version` and all decision fields.
- `FakeRefusalLog` records entries for assertions.
- `ApprovalPolicyTests` (pure, no repository): role gate, both sides of each limit, order of precedence,
  second-approver rule, Director-then-different-Director.
- `ApprovalServiceTests`: order of evaluation (Admin + missing reason → 403; unknown id with Admin → 403 not 404;
  missing reason on a non-pending invoice → 409, on a pending one → 400), submitter may reject, refusal log contents.
- Concurrency: a repository wrapper that bumps the stored version between read and save, plus one real
  `InMemoryInvoiceRepository` race with two Directors; exactly one succeeds, the loser gets 409 and the invoice holds one
  signature.
- `ApprovalsControllerTests`: status codes per FR-013, including Admin reject with no body → 403.

## Project Structure

### Documentation (this feature)

```text
specs/001-role-approval-limits/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── approvals-api.md
└── tasks.md             # /speckit-tasks, not created here
```

### Source Code

```text
backend/
├── src/
│   ├── InvoiceApp.Domain/    # ApprovalPolicy (+2 pure methods), ApprovalRefusal (+record), ApprovalService,
│   │                         # IRefusalLog (new), RefusalLogEntry (new)
│   └── InvoiceApp.Api/       # InMemoryRefusalLog (new), ApprovalsController (nullable reason), Program.cs (register)
└── tests/InvoiceApp.Tests/   # FakeInvoiceRepository.UpdateCount, FakeRefusalLog, extended test classes
```

**Structure Decision**: keep the existing three-project layout; add nothing outside it.

## Out of Scope

UC-1 bulk approval, UC-2 partial approval, an endpoint or UI for reading the refusal log, persistent log storage,
plaintext passwords and non-expiring sessions (already known and out of scope).
