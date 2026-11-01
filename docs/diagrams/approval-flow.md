# Approval flow: approve, refuse and second approval

Traced from the code on `feature/approval-limits-by-role` (HEAD `e4f60c8`). Every interaction cites a
file and symbol, with line numbers from that commit. Only behaviour that exists in the code is drawn.
`Reject` is not drawn: it shares the first steps (authority check, then lookup) but has its own rules.

Paths are relative to `backend/src/`. `Api` means `InvoiceApp.Api`, `Domain` means `InvoiceApp.Domain`.

```mermaid
sequenceDiagram
    autonumber
    actor Client as Client (UI or curl)
    participant MW as SessionAuthMiddleware
    participant Ctl as ApprovalsController
    participant Base as ApiControllerBase.Handle
    participant Svc as ApprovalService
    participant Pol as ApprovalPolicy
    participant Repo as InMemoryInvoiceRepository
    participant Log as InMemoryRefusalLog

    Client->>MW: POST /api/approvals/{invoiceId}/approve with Bearer token
    Note over MW: Api/Auth/SessionAuthMiddleware.cs InvokeAsync 16-34
    alt no valid session
        MW-->>Client: 401 (no body, the request stops here)
    else valid session
        MW->>Ctl: Approve(invoiceId)
        Note over Ctl: Api/Controllers/ApprovalsController.cs Approve 26-28. No RequireRole attribute (comment 11-13)
        Ctl->>Base: Handle(() => View(Approve(...)))
        Note over Base: Api/Controllers/ApiControllerBase.cs Handle 53-77
        Ctl->>Ctl: CurrentApprover() builds Approver(username, role) from the session
        Note over Ctl: ApprovalsController.cs CurrentApprover 39-43, Domain/Approver.cs record Approver
        Ctl->>Svc: Approve(invoiceId, approver)
        Note over Svc: Domain/ApprovalService.cs Approve 40-58

        Note over Svc,Pol: Step 1: authority, before the invoice is loaded
        Svc->>Pol: RefuseRole(approver)
        Note over Svc,Pol: ApprovalService.RefuseIfNotAuthorised 112-120, ApprovalPolicy.RefuseRole 72-77, CanApprove 60
        alt role has no entry in Approval:RoleLimits (for example Admin)
            Svc->>Log: Record(RefusalLogEntry)
            Note over Svc,Log: ApprovalService.Refuse 133-139, InMemoryRefusalLog.Record 11-14
            Svc-->>Base: throw ApprovalRefusedException(RoleCannotApprove)
            Base-->>Client: 403 with error and reason
            Note over Base: ApiControllerBase.cs catch ApprovalRefusedException 63-68. Invoice never loaded or saved
        else role has an entry in Approval:RoleLimits
            Note over Svc,Repo: Step 2: lookup and lifecycle
            Svc->>Repo: GetById(invoiceId)
            Note over Svc,Repo: ApprovalService.Find 155-157, InMemoryInvoiceRepository.GetById 32-35 returns a clone
            alt invoice not found
                Svc-->>Base: throw KeyNotFoundException
                Base-->>Client: 404 (ApiControllerBase.cs 59-62)
            else invoice found
                Svc->>Svc: EnsurePending(invoice)
                Note over Svc: ApprovalService.EnsurePending 162-168
                alt status is not PendingApproval
                    Svc-->>Base: throw InvalidOperationException
                    Base-->>Client: 409 (ApiControllerBase.cs 69-72)
                else status is PendingApproval
                    Note over Svc,Pol: Step 3: per-invoice rules, in this order
                    Svc->>Pol: RefuseApproval(invoice, approver)
                    Note over Svc,Pol: ApprovalService.RefuseIfNotPermitted 122-130, ApprovalPolicy.RefuseApproval 84-121
                    Note over Pol: 1 RefuseRole again (86). 2 IsSameUser(SubmittedBy, username) gives SelfApprovalNotAllowed (93). 3 any existing Approvals entry by the same user gives DuplicateApprover (100). Only then the amount rules below
                    alt amount above DualApprovalThreshold, the highest single-approver limit (RequiresTwoApprovers 69, DualApprovalThreshold 58)
                        Note over Pol: ApprovalPolicy.RefuseSignatureOnLargeInvoice 127-133. The role's own limit is not compared on this path
                        alt Approvals is empty (first signature)
                            Note over Pol: Any role that passed RefuseRole is accepted, Manager included
                        else one approval already recorded (second signature)
                            alt approver role is SecondApproverRole (IsSecondApproverRole 66-67)
                                Note over Pol: Accepted. Self-approval and duplicate checks above already ensured a different user
                            else any other role
                                Note over Pol: Refused with SecondApproverMustBeDirector (line 131)
                            end
                        end
                    else amount at or below DualApprovalThreshold
                        alt Amount greater than LimitFor(role) (LimitFor 63, comparison 115)
                            Note over Pol: Refused with AboveApprovalLimit
                        else Amount at or below the role limit
                            Note over Pol: Accepted. An amount equal to the limit passes
                        end
                    end
                    Pol-->>Svc: ApprovalRefusal or null

                    alt a rule refused
                        Svc->>Log: Record(RefusalLogEntry)
                        Note over Svc,Log: ApprovalService.Refuse 133-139. The log is separate from the invoice
                        Svc-->>Base: throw ApprovalRefusedException(reason)
                        Base-->>Client: 403 with error and reason
                        Note over Svc: No Approvals.Add and no repository Update were reached, so Status, Approvals, DecidedBy, DecidedAt, DecisionNote and Version are unchanged
                    else no rule refused
                        Svc->>Svc: invoice.Approvals.Add(ApprovalRecord)
                        Note over Svc: ApprovalService.cs line 48, on the clone returned by GetById
                        Svc->>Pol: RequiresTwoApprovers(amount)
                        Note over Svc,Pol: ApprovalService.ApprovalsAreComplete 141-142 needs Count of 2 above the threshold, else 1
                        alt signatures complete
                            Note over Svc: Status = Approved, DecidedBy = approver, DecidedAt = now (ApprovalService.cs 50-55)
                        else first of two signatures
                            Note over Svc: Status stays PendingApproval. FirstApprovedBy is derived from Approvals[0] (Domain/Invoice.cs line 20). InvoiceView sets AwaitingSecondApproval when Count is 1 (Domain/InvoiceView.cs line 43)
                        end
                        Svc->>Repo: Update(invoice)
                        Note over Svc,Repo: ApprovalService.Save 144-153, InMemoryInvoiceRepository.Update 57-72, under a lock
                        alt stored Version differs from invoice.Version (stale clone)
                            Repo-->>Svc: false, nothing stored
                            Svc-->>Base: throw InvalidOperationException (not logged as a refusal)
                            Base-->>Client: 409 (ApiControllerBase.cs 69-72, Repository 63-66)
                            Note over Repo: The stored invoice keeps the winning update. The losing request does not overwrite it
                        else stored Version equals invoice.Version
                            Repo-->>Svc: true, Version incremented and a clone stored (lines 68-70)
                            Svc-->>Ctl: updated Invoice
                            Ctl->>Svc: EligibilityFor(invoice, approver)
                            Note over Ctl,Svc: ApprovalsController.View 36-37, ApprovalService.EligibilityFor 87-106
                            Ctl-->>Client: 200 InvoiceView (InvoiceView.From, Domain/InvoiceView.cs 25-47)
                        end
                    end
                end
            end
        end
    end
```

## Dual-approval threshold versus a role's own limit

- A role's own limit is its entry in `Approval:RoleLimits`, read by `ApprovalPolicy.LimitFor` (line 63). It is the most that role may approve alone.
- `DualApprovalThreshold` (`ApprovalPolicy.cs` line 58) is a different value: `_roleLimits.Values.Max()`, the highest single-approver limit across all configured roles. With the shipped configuration that is the Director limit, 500,000.
- `RequiresTwoApprovers(amount)` (line 69) is `amount > DualApprovalThreshold`. Above it, one signature is never enough.
- Below or at it, only the approver's own limit decides (`RefuseApproval` lines 113-120). So a Manager (limit 50,000) is refused with `AboveApprovalLimit` at 200,000.00, while a Director passes.
- Above it, the role's own limit is not compared. The first signature may come from any role that passes `RefuseRole`, including a Manager. The second must come from a different user whose role is `SecondApproverRole` (`IsSecondApproverRole`, line 66-67), otherwise `SecondApproverMustBeDirector`.
- `SecondApproverRole` is configuration. The reason-code name and the UI text say "Director", but the policy compares against the configured role, not a literal.

## How the three scenarios map onto the diagram

| Scenario | Path through the diagram | Seeded example (`Api/InMemoryInvoiceRepository.cs` 16-24) |
|---|---|---|
| Approve (single signature) | Steps 1 to 3 pass, `ApprovalsAreComplete` is true, status becomes `Approved` | INV-1001, 4,250.00, a Manager or Director who is not `demo.admin` |
| Refuse | Any `throw ApprovalRefusedException` branch. `Approvals.Add` and `Update` are never reached | INV-1006, 200,000.00: a Manager attempts to **approve** it and is refused with `AboveApprovalLimit`. This is an approval attempt, not a rejection |
| Second approval | Above the threshold the first signature, by any approving role, leaves the invoice `PendingApproval`. The second must come from a different user whose role is the configured `SecondApproverRole`, then `ApprovalsAreComplete` is true | INV-1009, 900,000.00, already signed by `demo.manager`. A Director other than `demo.manager` completes it. A second Manager is refused with `SecondApproverMustBeDirector` |

## What the code does not do (so the diagram does not draw it)

- `RefuseRole` runs twice on the approve path: once in `ApprovalService.RefuseIfNotAuthorised` (line 112) and once inside `ApprovalPolicy.RefuseApproval` (line 86). The second call is redundant but harmless.
- A refusal is logged and thrown. A stale-version conflict (409) and a missing invoice (404) are **not** written to the refusal log, because only `ApprovalService.Refuse` calls `IRefusalLog.Record`.
- Nothing reads the refusal log: `InMemoryRefusalLog.Entries` (line 16) has no caller in `Api`.
- There is no lock across the lookup and the save, only a version check inside `Update`. If two approvals read the same invoice, whichever `Update` runs first takes the lock, finds the stored `Version` equal to its clone's, increments it and stores its clone. The other `Update` then sees a different stored `Version`, returns `false` and stores nothing, so `Save` throws `InvalidOperationException` and the client gets 409. The stored invoice keeps the winning update. This is not logged as a refusal.
- The UI role label ("needs a Director", `Api/wwwroot/app.js` line 37) is hard-coded and does not come from `SecondApproverRole`.
- Draft invoices cannot be approved: `EnsurePending` returns 409.

## Not verified

- The diagram was traced by reading the code, not by running a request. The revised Mermaid source (with nested alt/else branches) was rendered to SVG with the `mcp-mermaid` server and produced no syntax error. The rendered image was not inspected visually, and no request was run against the app.
- The concurrency path is traced from `InMemoryInvoiceRepository.Update`. No concurrent test was run for this document.
- Line numbers are from `e4f60c8` and will drift as the files change.
