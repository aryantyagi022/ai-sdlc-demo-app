# Contract: Approvals API

Both endpoints require sign-in. No `[RequireRole]`: authority comes from `Approval:RoleLimits`.

## POST `/api/approvals/{invoiceId}/approve`

No body. Returns `InvoiceView` on 200.

## POST `/api/approvals/{invoiceId}/reject`

Body: `{ "reason": "<text>" }`. The body and the property are optional at the binding layer so that the authority check
runs first; a blank or missing reason is rejected by the service (400) after authority and lifecycle.

## Outcomes (in evaluation order)

| Status | When | Body |
|---|---|---|
| 403 | `RoleCannotApprove` — role has no limit configured (e.g. Admin), approve or reject | `{ error, reason }` |
| 404 | invoice does not exist | `{ error }` |
| 409 | invoice is not `PendingApproval`; or stale write (concurrent decision lost) | `{ error }` |
| 400 | reject with missing/blank reason | `{ error }` |
| 403 | approve: `SelfApprovalNotAllowed`, then `DuplicateApprover`, then `SecondApproverMustBeDirector` / `AboveApprovalLimit` | `{ error, reason }` |
| 200 | decision saved | `InvoiceView` |

`reason` values are API surface: `SelfApprovalNotAllowed`, `DuplicateApprover`, `AboveApprovalLimit`,
`SecondApproverMustBeDirector`, `RoleCannotApprove`. `error` names the concrete limit or required role.

## Guarantees

- Any non-200 leaves `Status`, `Approvals`, `FirstApprovedBy`, `DecidedBy`, `DecidedAt`, `DecisionNote` and `Version`
  unchanged.
- Each 403 with a `reason` also appends one refusal-log entry (not exposed over HTTP).
- Concurrent competing decisions: exactly one 200; the rest 409. Which one wins is not guaranteed.
