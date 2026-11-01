# ANSWER KEY - do not open until scoring is finished

Base: feature/approval-limits-by-role at e4f60c8. Each diff is a patch applied to a /tmp clone only.

| Diff | Content | Planted bug? |
|---|---|---|
| diff-1 | Rename local `refusal` to `approvalRefusal` in `ApprovalService.EligibilityFor` | NONE (clean, behaviour-preserving) |
| diff-2 | `IsSameUser` in `ApprovalPolicy.cs` compares with `StringComparison.OrdinalIgnoreCase` instead of `Ordinal` | YES: spec says usernames compare ordinal, case-sensitive. `Demo.Manager` would count as `demo.manager` for self-approval and duplicate checks. The doc comment above still says "ordinal, case-sensitive". |
| diff-3 | `AboveApprovalLimit` check uses `invoice.Amount >= limit` instead of `>` in `ApprovalPolicy.RefuseApproval` | YES: limits are inclusive. 50,000.00 would be refused for a Manager, and 500,000.00 for a Director (also wrongly refuses instead of single-signature). |
| diff-4 | In `ApprovalService.Reject`, `Find(invoiceId)` moved before `RefuseIfNotAuthorised` | YES: authority must be checked before invoice lookup. A role that cannot approve (e.g. Admin) gets 404 for a missing invoice and 403 for an existing one, so it learns whether an invoice exists. Also breaks the RoleCannotApprove-before-lookup rule. |

## Run mapping

| Run | Diff | Repeat |
|---|---|---|
| R01 | diff-2 | 2 |
| R02 | diff-1 | 3 |
| R03 | diff-2 | 1 |
| R04 | diff-4 | 1 |
| R05 | diff-3 | 3 |
| R06 | diff-3 | 2 |
| R07 | diff-2 | 3 |
| R08 | diff-1 | 1 |
| R09 | diff-3 | 1 |
| R10 | diff-4 | 2 |
| R11 | diff-1 | 2 |
| R12 | diff-4 | 3 |

## Scoring suggestion
- Bug diffs: caught (named the exact defect and the rule it breaks) / partial (flagged the line but wrong reason) / missed.
- Clean diff: clean (no false defects) / false positive (reports a defect that is not there).
- Note the reviewer has no shell: each run was given the diff text inline, and the repo working tree does NOT contain the change.
