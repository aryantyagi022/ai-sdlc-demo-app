# Data Model: Approval Limits by Role

## Existing (unchanged shape)

**Invoice**: `Id`, `CustomerName`, `Amount`, `Status`, `CreatedAt`, `SubmittedBy`, `Approvals[]`, `DecidedBy`,
`DecidedAt`, `DecisionNote`, `Version`; derived `FirstApprovedBy` (= `Approvals[0].Username`, so it cannot drift).

**ApprovalRecord**: `Username`, `Role`, `ApprovedAt`.

**Approver**: `Username`, `Role` (from the session).

**ApprovalPolicy** (state from `Approval:RoleLimits`, `Approval:SecondApproverRole`):
`DualApprovalThreshold` = highest limit (500,000); role comparison ignores case.

## State transitions

```text
Draft --submit--> PendingApproval --final signature--> Approved
                       |  \--first signature above threshold--> PendingApproval (Approvals = 1)
                       \--reject (reason)--> Rejected   (Approvals kept)
```

A refused or conflicted attempt causes no transition and no write.

## New

**ApprovalRefusal** (record, pure value): `Reason` (`ApprovalRefusalReason`), `Message`.

**RefusalLogEntry** (record), stored separately from the invoice:

| Field | Notes |
|---|---|
| `InvoiceId` | as supplied in the request, even if no such invoice exists |
| `Username` | exact, un-normalised |
| `Role` | as in the session |
| `Action` | `Approve` or `Reject` |
| `Reason` | one of the five codes |
| `Message` | the same text returned to the caller |
| `OccurredAt` | UTC |

**Validation rules**

- Usernames: `StringComparison.Ordinal`, no trim. Roles: case-insensitive.
- `Reason` on reject: non-blank, checked after authority and lifecycle.
- Limits inclusive: `amount <= limit` passes; `amount > DualApprovalThreshold` needs two signatures.
- Duplicate check reads only `invoice.Approvals` of the invoice being decided.
