# UC-3: Multi-level invoice approval

## Background

Today any approver can approve any invoice, for any amount, on their own. Finance asked for approval
authority to reflect seniority, and for the largest invoices to need more than one signature.

## The change

Approval authority becomes graded by role, and invoices above the highest single-approver limit need
two different people to sign off.

## What the business asked for

1. A Manager can approve up to 50,000.
2. A Director can approve up to 500,000.
3. Above 500,000 needs two different approvers, and the second must be a Director.
4. The same user may never count as both approvers on one invoice.
5. A refused attempt gives a clear reason and changes nothing on the invoice.

## The rules in detail

### Roles

6. The existing `Approver` role becomes `Manager`. A new `Director` role is added.
7. `Admin` keeps its administrative duties, including deleting invoices, and may **not** approve.
8. Any signed-in user may still submit an invoice for approval. This is unchanged.

### Limits

9. Role limits are configured, not hard-coded, under `Approval:RoleLimits`.
10. Limits are **inclusive**: a Manager may approve exactly 50,000.00 but not 50,000.01. A Director may
    approve exactly 500,000.00 but not 500,000.01.
11. The two-approver threshold is derived as the highest configured single-approver limit. It is not a
    separate number.
12. Between the limits there is no two-approver path. A 200,000 invoice needs a Director specifically;
    two Managers may not combine to clear it.

### Two approvers

13. Above the threshold, the first signature may come from any Manager or Director. A role limit
    governs *solo* approval authority, not participation in a two-signature chain.
14. After the first signature the invoice stays in Pending Approval, records who signed, and shows that
    it still needs a second approver.
15. The second signature must come from a Director.
16. Only after the second signature does the invoice become Approved.

### The same user never counts twice — on one invoice

17. A user who has already approved **this invoice** may not approve it again.
18. This restriction is scoped to a single invoice. Approvals on other invoices place no restriction on
    a user: the same person may approve any number of different invoices, and may be the first approver
    on one invoice and the second approver on another.
19. A user may not approve an invoice they submitted themselves.
20. Where both rule 17 and rule 19 would apply, the submitter rule is reported, because it is the more
    specific reason.
21. Usernames are compared exactly (ordinal, case-sensitive).

### Rejection

22. Any Manager or Director may reject an invoice, alone, at any amount.
23. Rejection requires a reason.
24. Approvals already recorded on the invoice are kept as history when it is rejected, so the trail
    reads "Manager approved, Director rejected".

### A refused attempt changes nothing

25. Every check runs before any change is made, and a successful decision is saved exactly once.
26. An approval attempt that is refused leaves `Status`, `Approvals`, `FirstApprovedBy`, `DecidedBy`,
    `DecidedAt`, `DecisionNote` and `Version` byte-identical.
27. Refused attempts are not recorded on the invoice. They go to the log.
28. A refusal carries a reason code and a message that names the concrete fact behind it — the limit
    that was exceeded, or the role that is required.

### Reason codes

29. `SelfApprovalNotAllowed` — you submitted this invoice.
30. `DuplicateApprover` — you have already approved this invoice.
31. `AboveApprovalLimit` — the amount exceeds your role's limit and there is no two-approver path.
32. `SecondApproverMustBeDirector` — the second signature requires a Director.
33. `RoleCannotApprove` — your role has no approval authority.
34. Authority refusals (29–33) return **403**. Lifecycle conflicts — wrong status, or a competing
    decision saved first — return **409**. A missing invoice returns **404**. A missing rejection
    reason returns **400**.

### Concurrency

35. If two approvers act on the same invoice at the same time, exactly one decision is saved. The other
    is refused as a lifecycle conflict and changes nothing.

## Out of scope for the first release

- Escalation or expiry for an invoice left waiting at one of two signatures.
- Changes to how passwords are stored or how sign-in tokens expire.
- Email or in-app notifications.

## Where it touches the code

`ApprovalService` and the Domain model, `ApprovalsController`, `InvoicesController`,
`ApiControllerBase`, the in-memory repository, and the Approvals tab in the web UI.
