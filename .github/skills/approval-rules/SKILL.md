---
name: approval-rules
description: The invoice approval authority rules, their boundary values, and the eight places agents reliably get the negative rules wrong. Use before changing ApprovalService, ApprovalPolicy, the approvals endpoints, or any test that asserts who may approve what.
---

# Approval rules

AGENTS.md carries the rules table and the decisions behind it. **Read that first.** This skill
is about the part a table cannot express: the rules that say what must *not* happen, and the
specific ways they get broken.

Spec: `docs/use-cases/UC-3-multi-level-approval.md` — every rule below traces to a numbered line
there. Tests: `backend/tests/InvoiceApp.Tests/ApprovalServiceTests.cs`, tagged with those numbers.

## The boundary values, exactly

Limits are **inclusive**. Every one of these is a real test; do not change one without the other.

| Amount | Manager | Director |
|---|---|---|
| 50,000.00 | approves | approves |
| 50,000.01 | `AboveApprovalLimit` | approves |
| 500,000.00 | `AboveApprovalLimit` | approves |
| 500,000.01 | first signature only | first signature only |

`DualApprovalThreshold` is **derived** as the highest configured limit, never configured
separately. Adding a role with a higher limit moves the threshold. That is intentional.

## Validation order

Fixed, and the order is itself a rule:

```
role may decide at all   -> 403 RoleCannotApprove
invoice is PendingApproval -> 409
submitter is not approver  -> 403 SelfApprovalNotAllowed
not already an approver    -> 403 DuplicateApprover
limit / dual-approval path -> 403 AboveApprovalLimit | SecondApproverMustBeDirector
```

The role check runs **before the invoice is loaded**, in both `Approve` and `Reject`.

---

## The eight traps

### 1. Widening rule 4 into a cross-invoice approver history

The single most important negative rule, and the one most likely to be "helpfully" broken.

> The same user may never give both signatures **on one invoice**.

It is *not* "a user who has approved before may not approve again". One person may approve any
number of different invoices, and may be first approver on one and second on another.

```csharp
// Correct - reads this invoice only.
invoice.Approvals.Any(a => a.Username == approver.Username)

// Wrong - any lookup across invoices, any "has this person approved recently",
// any approver-history table. None of this exists, and none is wanted.
```

Tests **4.10** and **4.11** exist for no other purpose. If you widen the check they are the only
two that fail — so if you are editing this check, run them and watch.

### 2. Mutating before validating

Rule 5 is *a refused attempt changes nothing*. Not "mostly nothing".

Every refusal test snapshots the invoice and asserts `Status`, `Approvals`, `FirstApprovedBy`,
`DecidedBy`, `DecidedAt`, `DecisionNote` **and `Version`**. `Version` is the sharp one: it fails
even if the service wrote the same values back. Do all validation first, then mutate, then
`Update`.

### 3. Reading limits as exclusive

`amount <= limit`, not `<`. A Manager approving exactly 50,000.00 must succeed. Both sides of
both boundaries are tested; a one-character change here passes a careless review.

### 4. Assuming the first signature must be a Director

Above 500,000 a **Manager may give the first signature**. A role limit governs *solo* authority,
not participation. Only the second signature is restricted to a Director.

Corollary that also surprises people: a Director approving a 750,000 invoice alone does **not**
approve it. It stays `PendingApproval` at "1 of 2".

### 5. Duplicating role eligibility outside configuration

`Approval:RoleLimits` is the single source of truth for who may approve. A second copy — most
temptingly `[RequireRole("Manager","Director")]` on `ApprovalsController` — silently blocks any
role added to configuration later.

`[RequireRole]` is only for static permissions configuration does not own, such as `Delete` being
Admin only. The test for whether you got this right: **adding a role to `appsettings.json` and
nothing else must grant approval authority.**

### 6. Assuming rejection follows the approval ladder

It does not. **Any Manager or Director may reject any amount, alone**, including an invoice far
above their approval limit. Rejection also *keeps* approvals already recorded — so do not apply
"a refusal changes nothing" to the one path that legitimately writes history.

### 7. A reason code without the fact behind it

The message is shown to a person, so it must name the concrete limit or the required role:

> `Manager may approve up to 50,000.00; invoice INV-1006 is 200,000.00 and needs a higher approval level.`

not "Not allowed." Four e2e specs assert on that text. Codes alone also fail to kill Stryker's
string mutants.

### 8. Checking existence before authority

A role that cannot approve must get **403 for every invoice id**, including ones that do not
exist. Return 404 first and the endpoint becomes an existence oracle for anyone who can sign in.

---

## Before you call a rule change done

Break the rule and watch the right test fail. Do not reason about it.

```bash
dotnet test backend/InvoiceApp.sln --filter FullyQualifiedName~ApprovalServiceTests
```

See `dotnet-test-and-mutation` for how far to take that.
