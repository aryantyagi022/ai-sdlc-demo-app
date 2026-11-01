# Feature Specification: Approval Limits by Role

**Feature Branch**: `feature/approval-limits-by-role`

**Created**: 2026-10-31

**Status**: Draft

**Input**: User description: "Add approval limits by role. A Manager may approve invoices up to 50,000. A Director may approve up to 500,000. Anything above 500,000 needs two distinct approvers, and the second must be a Director. The same user may never count as both approvers for the same invoice. A refused attempt returns a clear reason and nothing about the invoice changes. Why: the sign-off level should match the money at risk."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Approve within my role's limit (Priority: P1)

A Manager or Director approves a pending invoice whose amount is within their limit, and it becomes approved with a single signature.

**Why this priority**: This is the everyday path and the core value: sign-off authority proportional to the amount.

**Independent Test**: Submit invoices of 50,000.00 and 500,000.00; a Manager approves the first, a Director approves the second; both become approved.

**Acceptance Scenarios**:

1. **Given** a pending invoice of 50,000.00, **When** a Manager approves it, **Then** it is approved.
2. **Given** a pending invoice of 50,000.01, **When** a Manager tries to approve it, **Then** the attempt is refused because it is above the Manager limit of 50,000.
3. **Given** a pending invoice of 50,000.01, **When** a Director approves it, **Then** it is approved.
4. **Given** a pending invoice of 500,000.00, **When** a Director approves it, **Then** it is approved with one signature.

---

### User Story 2 - Two approvers above 500,000 (Priority: P1)

An invoice above 500,000 needs two signatures from two different people. The first may be a Manager or a Director; the second must be a Director.

**Why this priority**: Highest money at risk, the strictest control.

**Independent Test**: Submit an invoice of 500,000.01; a Manager signs first, a Director signs second; it becomes approved only after the second signature.

**Acceptance Scenarios**:

1. **Given** a pending invoice of 500,000.01 with no signatures, **When** a Manager or Director approves, **Then** the first signature is recorded and the invoice stays pending.
2. **Given** one recorded signature, **When** a different Director approves, **Then** the invoice is approved.
3. **Given** one recorded signature, **When** a Manager (a different person) tries to give the second signature, **Then** it is refused because the second approver must be a Director.

---

### User Story 3 - Refusals explain themselves and change nothing (Priority: P1)

Whenever an approval is refused, the person sees a clear reason naming the concrete fact (the limit or required role), and the invoice is exactly as it was.

**Why this priority**: Users must be able to act on a refusal, and a refusal must never leave a partial approval.

**Independent Test**: Make each kind of refused attempt and compare the invoice before and after.

**Acceptance Scenarios**:

1. **Given** any refused attempt, **When** the invoice is read afterwards, **Then** its status, signatures, decision details and version are unchanged.
2. **Given** a refused attempt, **When** the reason is shown, **Then** it states the applicable limit or the required role.
3. **Given** a refused attempt made through the web page, **When** the page responds, **Then** it displays the message the server returned, and enabled or disabled controls follow the server's eligibility, not limits re-derived in the page.

---

### User Story 4 - The same person cannot sign twice (Priority: P1)

The same user cannot provide both signatures on one invoice. This applies only to that invoice; approving other invoices places no restriction.

**Why this priority**: Two-person control is meaningless if one person can fill both slots.

**Independent Test**: A Director signs an invoice above 500,000 and then tries again; refused. The same Director can still sign a different invoice.

**Acceptance Scenarios**:

1. **Given** a Director's first signature on a 600,000 invoice, **When** the same Director approves it again, **Then** it is refused as a duplicate approver.
2. **Given** a user who approved invoice A, **When** they approve invoice B, **Then** the duplicate rule does not apply.

---

### Edge Cases

- Amounts exactly at a limit (50,000.00 and 500,000.00) are within the limit; one cent above is not.
- Between 50,000.01 and 500,000 there is no two-approver path: two Managers cannot combine; a Director is required.
- The submitter cannot approve their own invoice; this is reported as a more specific reason than duplicate approver. The restriction applies only to approval: a submitter may reject their own invoice.
- A role with no approval authority (e.g. Admin) is refused for both approve and reject, without learning whether the invoice exists and before any rejection-reason check.
- Rejecting needs a reason and one Manager or Director at any amount, and keeps signatures already recorded.
- Two competing decisions on the same invoice at the same time: exactly one succeeds; the other is refused and changes nothing.
- Approving or rejecting an invoice that is not pending approval is a lifecycle conflict (409), distinct from an authority refusal, and changes nothing.
- A Director gives the first signature above 500,000 and a different Director gives the second: allowed, and the invoice is approved.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST allow a Manager to solely approve invoices up to and including 50,000.
- **FR-002**: System MUST allow a Director to solely approve invoices up to and including 500,000.
- **FR-003**: System MUST require two signatures from two different users for invoices above 500,000, where the second signer is a Director.
- **FR-004**: System MUST allow a Manager or Director to give the first signature above 500,000.
- **FR-005**: System MUST refuse a second signature from the same user on the same invoice, considering only that invoice's signatures.
- **FR-006**: System MUST refuse an invoice's submitter approving it (`SelfApprovalNotAllowed`). This applies only to approval; the submitter MAY reject their own invoice.
- **FR-007**: System MUST refuse roles without approval authority (reason `RoleCannotApprove`, including Admin, for approve and reject) before looking up the invoice and before validating a rejection reason.
- **FR-008**: System MUST make every refusal leave the invoice's status, approvals, first approver, decided-by, decided-at, decision note and version unchanged. Refusals are logged separately and never recorded on the invoice.
- **FR-009**: System MUST give each refusal one of these reason codes: `SelfApprovalNotAllowed`, `DuplicateApprover`, `AboveApprovalLimit`, `SecondApproverMustBeDirector`, `RoleCannotApprove`, with a message naming the limit or required role.
- **FR-010**: Role limits MUST come from configuration so that adding a role there grants approval authority without other changes.
- **FR-011**: System MUST allow a Manager or Director to reject at any amount, with a required reason, keeping recorded signatures. A missing reason is a bad request, not an authority refusal.
- **FR-012**: Role names MUST be compared ignoring case; usernames by exact, case-sensitive ordinal comparison with no trimming or normalization.
- **FR-013**: System MUST report authority refusals as forbidden (403), lifecycle conflicts as conflict (409), missing invoices as not found (404) and missing rejection reasons as bad request (400).
- **FR-014**: When competing decisions arrive for the same invoice concurrently, exactly one MUST succeed; the others MUST receive a conflict (409) and leave the invoice unchanged. Which request wins is not guaranteed.
- **FR-015**: Checks MUST run in this order, and the first failure decides the outcome:
  1. `RoleCannotApprove` (403), before invoice lookup and before rejection-reason validation.
  2. Invoice lookup: not found is 404.
  3. Lifecycle: an invoice that is not pending approval is 409.
  4. Rejection only: a missing or blank reason is 400. The self-approval and duplicate-approver checks do not apply to rejection.
  5. Approval only: `SelfApprovalNotAllowed`, then `DuplicateApprover`, then amount and second-approver eligibility (`AboveApprovalLimit`, `SecondApproverMustBeDirector`).
- **FR-016**: System MUST record each of the five coded authority refusals (FR-009) in the application's refusal log, stored separately from the invoice, with: invoice ID, attempting username, role, attempted action, refusal code, the clear message, and timestamp. Writing the log MUST NOT modify the invoice. Responses of 404, 409 and 400 carry no refusal code and are not entries in this log. This log is unrelated to any AI interaction log kept outside the application.
- **FR-017**: Approving or rejecting an invoice that is not pending approval MUST return a lifecycle conflict (409) and change nothing.
- **FR-018**: A Director MAY give the first signature above 500,000 and a different Director the second, completing approval.

### Key Entities

- **Refusal log entry**: invoice ID, attempting username, role, attempted action, refusal code, message, timestamp; separate from the invoice.
- **Invoice**: amount, submitter, status (Draft, PendingApproval, Approved, Rejected), recorded approvals, first approver, decision details (decided-by, decided-at, note), version.
- **Approval**: one signature by one user with a role on one invoice.
- **Role limit**: the largest amount a role may approve alone.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of invoices at or below a role's limit are approvable in a single action by that role; 100% above it are refused.
- **SC-002**: 100% of invoices above 500,000 reach approved only after two different people sign, the second a Director.
- **SC-003**: In 100% of refused attempts the invoice is identical before and after.
- **SC-004**: Every coded refusal message is checkable for its concrete facts: `AboveApprovalLimit` states the invoice amount and the configured limit; `SecondApproverMustBeDirector` states the invoice amount and the required role; `SelfApprovalNotAllowed` and `DuplicateApprover` name the user and the invoice; `RoleCannotApprove` names the role (it cannot state an amount, because it is decided before the invoice is looked up).
- **SC-005**: Boundary amounts on both sides of each limit behave as specified.

## Assumptions

- The limits (50,000 and 500,000) and roles are existing configuration values (`Approval:RoleLimits`) and may change there.
- The second-approver role is configurable (`Approval:SecondApproverRole`) and defaults to Director. Under the current configuration the second approver above 500,000 must be a Director, as stated in FR-003.
- Users are already signed in with a role; sign-in is out of scope.
- Admin is administrative only: it cannot approve or reject.
- Out of scope: bulk approval and partial approval.
- Currency is a single unit; no conversion.
- Approving one invoice never restricts the same user on another invoice.
