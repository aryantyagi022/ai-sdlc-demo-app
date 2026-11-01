# Review findings: feature/approval-limits-by-role

Findings F1-F10 from three review passes of the branch against `main`. No confirmed defects were identified.
Verdicts are as agreed in chat; this file records them in the repository so they do not depend on a PR description.

## Review passes

| Pass | Reviewer | Access | What it did |
|---|---|---|---|
| 1 | `pr-reviewer` | no shell, no diff inspection, no tests | Code reading only; produced F1-F7 |
| 2 | `general-purpose` agent (substituted because `pr-reviewer` has no shell), fresh context | read-only shell | Reviewed `git diff main...HEAD`; ran `dotnet test backend/InvoiceApp.sln`: 136 passed, 0 failed; no confirmed defects; classified F1-F7 |
| 3 | `pr-reviewer` | no shell, no tests | Supplementary code-reading review; added F8-F10 |

- `node --check backend/src/InvoiceApp.Api/wwwroot/app.js` passed.
- Playwright was not run during any review pass.
- Mutation testing was not part of the review passes.

## Findings

| ID | Severity | Finding | Verdict | Resolution |
|---|---|---|---|---|
| F1 | Low | Configurable second-approver role is not exposed in the UI | Deferred | The UI text "needs a Director" is hard-coded in `app.js`; correct for the shipped configuration |
| F2 | Low | Refusal log has no read path or size limit | Deferred | Recording is required; read access and retention are out of scope |
| F3 | Low | Second-approver role is not required to hold the highest limit | Dismissed | The specification does not require it |
| F4 | Low | Null `Role` might throw instead of returning 403 | Dismissed | Normal `DemoUser.Role` defaults to an empty string. Null-role behaviour was inferred, not experimentally tested |
| F5 | Low | Invoice submit handler has no try/catch for 409/404 | Dismissed | Pre-existing on `main` |
| F6 | Info | An existing test checks the refusal-log count but not the specific `SecondApproverMustBeDirector` reason code | Deferred | Test-coverage suggestion; reason-code logging was reasoned about, not experimentally tested |
| F7 | Low | Stale source line references in the `tasks.md` Polish verdict table | Fixed | Corrected in commit `e4f60c8` |
| F8 | Low | The `app.js` approval-button fallback can display an Approve button when eligibility is missing | Dismissed | Potentially confusing UI, not an authorization bypass; the server still enforces the approval rules |
| F9 | Info | Redundant `RefuseRole` check at `ApprovalPolicy.cs:85` | Dismissed | Harmless |
| F10 | Info | Redundant null guards | Dismissed | No demonstrated defect |

## Classification

- Confirmed design trade-offs (deferred): F1, F2.
- Pre-existing and dismissed: F5.
- Fixed: F7.
- Dismissed: F3, F4, F8, F9, F10.
- Test-coverage suggestion (deferred): F6.

Two further design trade-offs are recorded in `specs/001-role-approval-limits/tasks.md` ("Polish review verdicts"): any configured role may give the first signature above the threshold, and the dual-approval threshold is the highest configured single-approver limit. They are not repeated as findings here.

## Uncertainty

- F4: null-role behaviour was not experimentally tested.
- F6: reason-code logging was reasoned about, not experimentally tested.
- Race-test determinism was not independently verified during review.
- Passes 1 and 3 could not run commands, so their findings rest on reading code.
