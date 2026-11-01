# Assignment write-up: approval limits by role

Markers used below: **[not in repo]** means the evidence exists only in chat or in `/tmp` (see the checklist in section 13); **[uncertain]** means the claim is not directly verified.

Branch `feature/approval-limits-by-role`, compared against `main` (`66cb3f9`).

## 1. What was built

Invoice approval authority now depends on role and amount. Specification: `specs/001-role-approval-limits/spec.md`.

| Amount | Who may approve | Signatures |
|---|---|---|
| up to and including 50,000 | Manager or Director | 1 |
| 50,000.01 to 500,000 | Director only | 1 |
| above 500,000 | first: Manager or Director; second: a different user with the configured second-approver role (Director) | 2 |

- Admin cannot approve or reject. Limits come only from `Approval:RoleLimits`.
- A refused attempt returns a reason code (`SelfApprovalNotAllowed`, `DuplicateApprover`, `AboveApprovalLimit`,
  `SecondApproverMustBeDirector`, `RoleCannotApprove`) and leaves the invoice unchanged. Refusals go to a separate log.
- Flow with file and symbol citations: `docs/diagrams/approval-flow.md`.

## 2. Requirements and proof

### 2A. Assignment deliverables

| Requirement | Where covered | Status |
|---|---|---|
| Link every requirement to proof | This table and 2B | covered |
| Context experiment | Section 5, `ai-logs/context-experiment/` | covered |
| Agent mistakes and corrections | Section 6 | covered (agent-reported, see section 6) |
| All three mutation scores | Section 7 | covered (three separate runs) |
| Review verdicts | Section 8, `docs/reviews/review-findings.md` | covered |
| Reviewer evaluation | Section 9 | covered in text; raw outputs and key are in `/tmp` **[not in repo]** |
| Quote human decisions exactly | Section 4 | covered; checked against the log |
| Demo video, 8-10 minutes | Section 12 | **open**: not yet recorded; link is a TODO placeholder |

### 2B. Feature specification requirements (FR/SC)

Proof is by test file and task ID. The mapping is file-level except where test names are given; I did not re-verify each individual test name **[uncertain]**.

| Requirement | Spec | Proof |
|---|---|---|
| Manager approves up to 50,000, Director up to 500,000, inclusive | FR-001, FR-002, SC-001, SC-005 | `ApprovalPolicyTests.cs`, `e2e/tests/approval-limits.spec.ts`; break "threshold `>` to `>=`": 3 tests failed (`docs/testing/phase-b-break-rules.txt` f) |
| Above 500,000 needs two distinct approvers, second a Director | FR-003, FR-004, FR-018, SC-002 | T030, T032 (`ApprovalPolicyTests.cs`, `ApprovalServiceTests.cs`), `e2e/tests/two-approver.spec.ts`; breaks a, c, d: 5, 16, 22 tests failed |
| Same user never signs twice on one invoice, other invoices unaffected | FR-005 | T031; break b: 11 tests failed; Phase C break 5: 2 specs failed |
| Submitter cannot approve; rejection by submitter allowed | FR-006 | `backend/tests/InvoiceApp.Tests/ApprovalServiceTests.cs`: `Approve_RefusesTheUserWhoSubmittedTheInvoice` (:177), `Reject_TheSubmitterMayRejectTheirOwnInvoice` (:615); `ApprovalPolicyTests.cs`: `RefuseApproval_RefusesTheSubmitterAndNamesTheUserAndInvoice` (:164), `RefuseApproval_ReportsSelfApprovalNotDuplicateWhenTheSubmitterAlsoAlreadySigned` (:177) |
| Unauthorized role is refused before invoice lookup (approve and reject) | FR-007 | `backend/tests/InvoiceApp.Tests/ApprovalServiceTests.cs`: `Approve_RefusesAnIneligibleRoleWithoutRevealingWhetherTheInvoiceExists` (:710), `Reject_RefusesAnIneligibleRoleWithoutRevealingWhetherTheInvoiceExists` (:720), both on an unknown invoice with an Admin; `ApprovalsControllerTests.cs`: `Approve_AnIneligibleRoleCannotUseTheEndpointToDiscoverWhichInvoicesExist` (:129, 403 for a missing and an already-decided invoice) |
| Unauthorized role is refused before the rejection reason is validated | FR-007, FR-015 | `ApprovalServiceTests.cs`: `Reject_ChecksAuthorityBeforeTheReasonIsValidated` (:731, Admin with a blank reason gets `RoleCannotApprove`), `Reject_ARefusedRoleIsLoggedWithTheRejectAction` (:681, Admin, unknown invoice and blank reason); `ApprovalsControllerTests.cs`: `Reject_AnAdminWithNoBodyOrNoReasonIsRefusedForRole` (:203, 403) |
| Not-found and lifecycle conflicts are reported before a missing reason (a different rule, FR-015) | FR-015 | `ApprovalServiceTests.cs`: `Reject_ReportsAMissingInvoiceBeforeAMissingReason` (:571), `Reject_ReportsALifecycleConflictBeforeAMissingReason` (:579); both use a Director, so they do **not** prove the role-first rule. Red-run provenance: `docs/testing/phase-a-red-run.txt` item 3 |
| Refusals change nothing, including `Version` | FR-008, SC-003 | `InvoiceAssert.cs` used by `ApprovalServiceTests.cs` and T032; quickstart rows 1-9 (T047) |
| Reason codes with concrete messages | FR-009, SC-004 | `ApprovalPolicyTests.cs`; `e2e/tests/refusals.spec.ts` asserts the server text on the page |
| Limits come from configuration | FR-010 | `ApprovalPolicy` constructor tests; `docs/mutation/after.txt` break-verification |
| Rejection by Manager or Director at any amount, reason required | FR-011 | `ApprovalServiceTests.cs`, `ApprovalsControllerTests.cs` |
| Role case-insensitive, username ordinal | FR-012 | break g: 3 tests failed (`phase-b-break-rules.txt`); reviewer evaluation diff 2 |
| 403 / 409 / 404 / 400 mapping | FR-013, FR-017 | `ApprovalsControllerTests.cs` |
| Concurrent decisions: exactly one succeeds, loser unchanged | FR-014 | T033, T034 (`Barrier(2)` race test); breaks e1, e2; 20 of 20 unmodified runs passed, 5 of 5 broken runs failed |
| Refusals recorded in a log separate from the invoice | FR-016 | Production: `IRefusalLog` (`backend/src/InvoiceApp.Domain/IRefusalLog.cs`), written by `ApprovalService.Refuse` (`ApprovalService.cs:133-138`), implemented by `InMemoryRefusalLog` (`backend/src/InvoiceApp.Api/InMemoryRefusalLog.cs`), registered at `Program.cs:15`. Tests of recording (use the test double `FakeRefusalLog`, not the production class): `ApprovalServiceTests.cs` `Approve_ARefusalWritesExactlyOneLogEntryWithTheSameMessage` (:647), `Approve_ADuplicateApproverRefusalIsLogged` (:670), `Reject_ARefusedRoleIsLoggedWithTheRejectAction` (:681), `NotFoundConflictAndBadReasonAreNotWrittenToTheCodedRefusalLog` (:694), `Approve_ASecondSignatureThatLosesTheVersionRaceChangesNothingAndIsNotLoggedAsARefusal` (:842). Phase A red category 2 (5 failures; test names not recorded) |
| UI shows eligibility and refusal reasons, never re-derives rules | spec US3 | `e2e/tests/*.spec.ts`, notably `refusals.spec.ts:106` (server refuses an over-limit approval the page wrongly offered; invoice stays Pending Approval, 0 of 1); `docs/testing/phase-c-break-rules.txt` (6 breaks, all killed) |

**Evidence flags (claims without direct proof)**
- `InMemoryRefusalLog` has no test of its own, and no test reads the log through the API, so the production registration (`Program.cs:15`) and the real class are not covered by any assertion **[no direct evidence]**.
- No recorded break-the-rule result names the role-first tests above. T029 lists "drop the role gate (T010, T014)" but the per-test results were not saved; only the reason-above-`Find` break is recorded as caught, by the two FR-015 tests **[uncertain]**. The reviewer-evaluation diff 4 (`Find` before the authority check) is a review-detection result, not a test result, and is cited only in section 9.
- Phase A red-run test names are "not recorded" for items 1, 2 and 4 (`phase-a-red-run.txt`).
- The e2e row for the UI cites `e2e/tests/*.spec.ts` as a group; I did not map each specification to a requirement **[uncertain]**.

### Test results

| Suite | Result | When |
|---|---|---|
| `dotnet test backend/InvoiceApp.sln` | 136 passed, 0 failed | re-run at HEAD `9169ff3` on 2026-11-01 |
| `dotnet test` at the clean clone of `b723c62` | 135 passed | T047 (before the one Polish test) |
| `cd e2e && npm test` | 17 specs passed | Phase C commit `b723c62` (T047, clean clone) |
| `cd e2e && npx playwright test --reporter=list` | 17 passed, 0 failed, 0 skipped, 27.6 s; Chromium, 1 worker | re-run after Polish at HEAD `9169ff3` on 2026-11-01 |
| Quickstart table rows 1-9 | all matched | T047, curl on a fresh server |

Required over-limit UI test (run 10 of 17 in the 2026-11-01 run): `tests/refusals.spec.ts:106`, "an over-limit approval the page wrongly offered is refused by the server and the page shows why". It deliberately intercepts the invoice list so the page tells a Manager they may approve a 200,000 invoice, then clicks Approve. It asserts that the server refuses with the message naming the 50,000.00 limit and 200,000.00, and that the invoice is still "Pending Approval" with "0 of 1" approvals. It does **not** assert `Version` or every decision field (`DecidedBy`, `DecidedAt`, `DecisionNote`, `FirstApprovedBy`); those are asserted in the .NET tests (`InvoiceAssert.AssertUnchanged`), not in this UI test.

Baseline before the feature: 7 .NET tests (`docs/mutation/before.txt`).
Phase A red run: 118 tests, 18 failed, 100 passed. This was captured from the session; the raw output was not saved, so it cannot be reproduced **[uncertain]** (`phase-a-red-run.txt`, PROVENANCE).

## 3. Process (Spec Kit and phases)

| Stage | Artifact |
|---|---|
| Specify, clarify | `specs/001-role-approval-limits/spec.md`, `checklists/requirements.md` |
| Plan | `plan.md`, `research.md`, `data-model.md`, `contracts/approvals-api.md`, `quickstart.md`; earlier planning in `docs/plan/PLAN-multi-level-approval.md` |
| Tasks | `tasks.md` (T001-T048, all checked) |
| Phase A (single-approver limits) | `ac153c4` |
| Phase B (two-approver rule) | `7fde0c3` |
| Phase C (UI and e2e) | `b723c62` |
| Polish, mutation | `6d631de`, `2669273` |
| Evidence | `e90d698` (redacted logs), `e4f60c8` (line references), `9169ff3` (diagram) |

## 4. Human decisions, quoted from `ai-logs/human-decisions.jsonl`

Quotes are the log's `answer` text, minus the `User responded: ` prefix the hook adds. `L` is the line in the file.
Provenance, checked against both the working-tree file and `git show HEAD:ai-logs/human-decisions.jsonl`: the two are byte-identical (41 lines each), so **every quote below is committed** (in `e90d698`); there are no uncommitted additions to this file. Every quote is an exact match for its line in both copies.
Rows marked † are **back-filled from chat** (`source: chat_message_backfill`, `original_timestamp: null`, all stamped `2026-11-01T06:21:59Z`). L39 is a labelled reconstruction (`verbatim: false`), not a quotation, and is listed below the table, not in it.
All other rows are original `ask_user` answers. L41 is one of those: `source: ask_user`, `2026-11-01T06:28:38Z`, committed, and not back-filled.

| L | Decision area | Exact words |
|---|---|---|
| 15 | Role strategy (supersedes L14) | "I think we need to change the strategy, Existing approvers become Managers, with a 50,000 approval limit. Add Director as a new role. And keep Admin separate" |
| 16 | Invoice state for the first signature | "Approvals collection + PendingApproval, plus FirstApprovedBy, plus needs second approval" |
| 17 | First and second approver above 500,000 | "Any Manager or Director may give the first approval; the second must be a Director" |
| 18 | Rejection | "Any Manager or Director may reject any amount, alone; prior approvals kept as history" |
| 19 | Self-approval and duplicates | "Keep both — submitter cannot approve, and the same user cannot approve twice" |
| 20 | Boundaries and config | "Inclusive limits, configured per role in appsettings.json" |
| 21 | Refusal surface | "Reason codes + 403 for authority refusals, 409 for lifecycle conflicts" |
| 22 | Demo accounts | "demo.manager, demo.director, demo.director2, demo.admin + two large seeded invoices" |
| 26 | Break-the-rule discipline | "mandatory" |
| 28 | Review style | "adversarial" |
| 29 | MCP restrictions | "Pin @playwright/mcp to a fixed version, Remove browser_run_code_unsafe and browser_evaluate from playwright, Document the trifecta risk in AGENTS.md" |
| 30 | Context experiment score | "before_score=3, after_score=4.3, notes=It does rejection and the reasoning is more elaborate and concrete" |
| 32 † | Spec check (3 of the bullets) | "Approval authority comes solely from Approval:RoleLimits.\nThe same user cannot approve the same invoice twice, but may approve other invoices.\nConcurrent competing decisions must allow exactly one successful decision; the losing request must not change the invoice." |
| 33 † | Refusal log growth | "Record the in-memory refusal log's unbounded growth as a deferred limitation. Do not redesign it now." |
| 34 † | Phase A scope | "Do not expand Phase A to cover SecondApproverMustBeDirector; that belongs to Phase B." |
| 35 † | Race test | "Do not assert which Director wins." |
| 36 † | Documentation findings | "Yes, fix T039 and T040. Change only specs/001-role-approval-limits/tasks.md. Don't change application code or add new tests just to address these documentation findings." |
| 38 † | Polish scope | "Don't commit or push yet.\nKeep .playwright-mcp out of this change." |
| 41 | Redaction exception | "Allow the scripted exception" |

Superseded early answers, kept so the history is not rewritten:
- L10 "Yes — Approver up to a configured limit, Admin above it" and L12 "50000" were replaced by L14 and then L15.
- L14 "Keep Approver, Admin role, add Manager and Director, both have approval rights(but roles mentioned above applies ), keep admin an Admin only, with his tasks," was replaced by L15.

Also in the log: skill content choices L25 (8 traps) and L27 (7 traps), and the back-filled, **reconstructed** L39 (`verbatim: false`, not the user's exact words): "leave LOW findings D1 and U1 unchanged".
L31 and L37 are also back-filled and not quoted here.
Cancelled prompts (L6, L7, L40) are recorded as "User cancelled the request."

## 5. Context experiment (`ai-logs/context-experiment/`)

Identical prompt on two commits: *Can a Manager approve an invoice of 75,000? What happens to the invoice if they try, and which command runs the tests? Cite files.*

| | Before (`66cb3f9`, main) | After (`ca90faa`) |
|---|---|---|
| `AGENTS.md` | 38 lines | 158 lines |
| Answer | "No: a Manager can't approve any amount" (no Manager role existed) | "No: 75,000 is in the 50,000.01-500,000 band, Director only" |
| Amount considered | No | Yes, names the limit and the band |
| Refusal mechanism | `[RequireRole]` route filter, 403 | `ApprovalRefusedException` / `AboveApprovalLimit`, 403 `{ error, reason }` |
| Invoice state | Untouched (filter never reaches the service) | Untouched; lists the six fields |
| Human score | 3 / 5 | 4.3 / 5 |

Caveats (from `diff.md`): the two commits differ in more than `AGENTS.md` (code, skills, agents, hooks), so the gain is not attributable to `AGENTS.md` alone;
the before answer was correct for its commit; one run each, no repeats. The 4.3 on a 1-5 scale is the human's own figure, recorded as given.

## 6. Agent mistakes and corrections

| # | Mistake | How it was caught | Correction | Evidence |
|---|---|---|---|---|
| 1 | Agent proposed widening the duplicate rule to a cross-invoice approver history | Human corrected it in planning (agent's own account) | Per-invoice scope written into AGENTS.md and the `approval-rules` skill | `human-decisions.jsonl` L25 |
| 2 | Agent's recommended authorisation design and a `Reject` that was not hoisted like `Approve` | `rubber-duck` agent (agent's own account) | Option C changed to B+; authority checked first in `Reject` too | `human-decisions.jsonl` L28 |
| 3 | `FakeInvoiceRepository.Update` was a no-op, hiding three mutants | Break-the-rule step (agent's own account) | Real `Update` with version check | `human-decisions.jsonl` L26 |
| 4 | Green e2e suite while "Sign out" showed to signed-out visitors | Opening the rendered page | Rule added to `playwright-e2e` skill | `human-decisions.jsonl` L27 |
| 5 | Phase A: blank reason validated before invoice lookup; reject with no body returned 415 | Red run | Order fixed to match FR-015; optional body | `docs/testing/phase-a-red-run.txt` |
| 6 | Race test used `Task.WhenAll` and was not guaranteed to overlap | Break e2 only failed it by chance | Rewritten with `Barrier(2)`; 20 of 20 runs | `phase-b-break-rules.txt` B |
| 7 | New e2e spec expected "None" where the page shows "0 of 1" | Red run | Test corrected, no UI change | `phase-c-break-rules.txt`, T041 |
| 8 | `after.txt` mislabelled line 97 and line 92 survivors, and claimed completion early | Human review | Corrected in `2669273` | `docs/mutation/after.txt`, tasks.md verdict 5 |
| 9 | Stale line references in the Polish table | Review pass 2 | Corrected in `e4f60c8` | `tasks.md` Polish table |
| 10 | `pr-reviewer` has no shell, so pass 1 never saw the diff; pass 3 reported an "unterminated template literal" in `app.js` | Substituted a shell-capable agent; `node --check app.js` passed | Recorded as a limitation, finding dismissed | `docs/reviews/review-findings.md` (proposed) |
| 11 | Playwright MCP calls failed with missing `target` arguments and a click timeout; Phase C used a script and screenshots instead | Tool errors in the log | Recorded in T043 | `ai-interactions.jsonl` (2026-10-30T17:56-18:08), `tasks.md` T043 |

**Agent-reported, not independently verified.** Every row except row 8 is the agent's own account, taken from the log or from documents the agent wrote; none was independently verified **[uncertain]**. Row 8 was caught by human review, though the correction itself was agent-written. Rows 1-4 are quoted from `ask_user` prompts in the decisions log.

## 7. Mutation testing (Stryker 4.13.0, `backend/src/InvoiceApp.Domain`)

Score = killed / (killed + survived + no coverage); ignored and compile-error mutants are excluded. I recomputed each figure from the counts.

These are **three separate Stryker runs**. Each row is one run's own result. Comparing rows is a separate, weaker step (see the caveat below).

| Run | Test count | Killed | Survived | No coverage | Ignored | Compile error | Overall | ApprovalService | ApprovalPolicy | Verified against |
|---|---|---|---|---|---|---|---|---|---|---|
| 1. Baseline | 7 | 12 | 10 | 17 | 12 | 0 | 30.77% (12 / 39) | 60.00% (12 / 20) | none (file did not exist) | `docs/mutation/before.txt` counts only; its Stryker report is not retained |
| 2. After Phases A-C, before the Polish test | not recorded | 86 | 22 | 20 | 31 | 12 | 67.19% (86 / 128) | 83.33% (35 / 42) | 82.14% (46 / 56) | `StrykerOutput/2026-11-01.11-25-27/reports/mutation-report.json`, recomputed |
| 3. Final | 136 | 93 | 17 | 18 | 31 | 12 | 72.66% (93 / 128) | 83.33% (35 / 42) | 94.64% (53 / 56) | `StrykerOutput/2026-11-01.11-30-09/reports/mutation-report.json`, recomputed; matches `after.txt` |

- Run 1 baseline total is 51 mutants (12 + 10 + 17 + 12), consistent with `before.txt`.
- Runs 2 and 3 differ only in `ApprovalPolicy.cs` (+7 killed: 5 survived and 2 no-coverage mutants became killed); nothing else changed.
- `StrykerOutput` is git-ignored, so runs 2 and 3 can be checked locally only. The report JSON has no date field; the dates come from the directory names. The test count for run 2 is not recorded **[uncertain]**.
- Run 3 date: 2026-11-01 (directory `2026-11-01.11-30-09`).
- **Caveat on comparing runs:** the baseline and later populations differ (the baseline predates `ApprovalPolicy` and ran 7 tests; its 51 mutants versus 128 scored plus 31 ignored plus 12 compile-error mutants later), so baseline-to-final is indicative, not like-for-like. Runs 2 and 3 are the same mutant population and are comparable with each other.
- **ApprovalService 83.33% is below the 90% target** in `docs/plan/PLAN-multi-level-approval.md:258`, which also asked for zero survivors on the distinctness check and refusal paths. That was not fully met: line 97 survives (not-pending branch of `EligibilityFor`) and line 92 has no coverage (role-refusal branch of `EligibilityFor`).
- Equivalent-mutant verdicts (`ApprovalPolicy.cs:137`, `Invoice.cs:5-6`) are by argument only, not proven.
- Break-verification of the Polish test: 3 hand-applied breaks in an isolated copy, all failed the expected test. The other constructor messages were not individually broken.
- `docs/mutation/after-ai-tests.txt` was never created. `after.txt` and the break-verification cover its purpose.

## 8. Review verdicts

Review passes (none ran Playwright):

| Pass | Reviewer | Access | Result |
|---|---|---|---|
| 1 | `pr-reviewer` | no shell, no diff, no tests | 7 findings (F1-F7) |
| 2 | `general-purpose` (substituted, shell-capable), fresh context | read-only, actual `git diff main...HEAD` | no confirmed defects; `dotnet test`: 136 passed |
| 3 | `pr-reviewer` | no shell, code reading only, no tests | no defects; added F8-F10 |

`node --check backend/src/InvoiceApp.Api/wwwroot/app.js` passed. No confirmed defects were identified.

| ID | Severity | Finding | Verdict | Resolution |
|---|---|---|---|---|
| F1 | Low | Configurable second-approver role is not exposed in the UI | Deferred | Hard-coded "needs a Director" at `app.js:37`; correct for the shipped configuration |
| F2 | Low | Refusal log has no read path or size limit | Deferred | Recording is required; read and retention are out of scope (decision L33) |
| F3 | Low | Second-approver role not required to hold the highest limit | Dismissed | The specification does not require it |
| F4 | Low | Null-role behaviour | Dismissed | Normal `DemoUser.Role` defaults to empty string; null-role behaviour was inferred, not experimentally tested **[uncertain]** |
| F5 | Low | Submit handler has no try/catch for 409/404 | Dismissed | Pre-existing on `main` |
| F6 | Info | Test checks refusal-log count, not the `SecondApproverMustBeDirector` code | Deferred | Test-coverage suggestion; reasoned about, not experimentally tested **[uncertain]** |
| F7 | Low | Stale line references in the tasks.md Polish table | Fixed | `e4f60c8` |
| F8 | Low | `app.js` fallback can show an Approve button when eligibility is missing | Dismissed | Potentially confusing UI, not an authorization bypass; the server still enforces the rules |
| F9 | Info | Redundant `RefuseRole` check at `ApprovalPolicy.cs:85` | Dismissed | Harmless |
| F10 | Info | Redundant null guards | Dismissed | No demonstrated defect |

Two design trade-offs from the Polish review are deferred and recorded in `tasks.md` ("Polish review verdicts"): any configured role may sign first above the
threshold (`ApprovalPolicy.cs:128`), and the threshold is the highest configured limit (`ApprovalPolicy.cs:58`).
Race-test determinism was not independently verified during review **[uncertain]**.
The same F1-F10 table is recorded as repository evidence in `docs/reviews/review-findings.md`.

## 9. Reviewer evaluation (`pr-reviewer`, 12 fresh-context runs)

Method: three small diffs with one planted bug each plus one clean diff, three runs each, applied only to a clone under `/tmp`, never to the feature branch.
Each reviewer was given the diff inline and the prompt "Review this PR against our checklist. Report findings by severity with file and line, suggest a fix for each, and say what you are unsure about."

| Diff | Change | Runs | Human score |
|---|---|---|---|
| diff-1 | Rename a local variable in `EligibilityFor` (clean) | R02, R08, R11 | all clean |
| diff-2 | `IsSameUser` uses `OrdinalIgnoreCase` (breaks FR-012) | R01, R03, R07 | all caught |
| diff-3 | `>=` instead of `>` for the limit (breaks inclusive limits) | R05, R06, R09 | all caught |
| diff-4 | `Find` moved before the authority check in `Reject` (breaks FR-007) | R04, R10, R12 | all caught |

Human assessment, in the user's words (chat, not in the log): "My assessment is 2/2 for every run: all 9 planted bugs were detected, and all 3 clean-diff reviews correctly identified the harmless rename."

Limits: small diffs against rules written in `AGENTS.md`, three runs per diff, the diff supplied inline, no tests executed (the reviewer has no shell),
and several runs said they had not opened `AGENTS.md` or the spec. 12 of 12 is not a miss rate.
Outputs, patches and the answer key are in `/tmp/pr-eval/` and `/tmp/pr-eval-key/` **[not in repo]**.

## 10. Agents, skills and tools actually used

From `ai-logs/ai-interactions.jsonl` (904 records; the `model` field is `unknown` on every record, so models are not recorded):

| Item | Evidence |
|---|---|
| `rubber-duck` agent | 70 hook records |
| `explore` agent | 30 hook records |
| `test-critic` agent | 29 hook records (one attempt failed: `"name": Required`) |
| Skills | `ai-interaction-log` (3), `pr-self-review` (2), `dotnet-test-and-mutation` (1). Spec Kit skill use is not recorded in the log's `skills_used` field **[uncertain]** |
| Playwright MCP | 16 calls on 2026-10-30, several failed; Phase C used a script and screenshots (T043) |
| `browser_evaluate` | 1 call on 2026-10-30T17:58, before `74feeba` (2026-10-31) removed it from the allowed tools |

Counts are hook records, not distinct runs. The seven custom agents in `.github/agents/` are not claimed as run except where the log shows it **[uncertain]**.

## 11. Known limitations

- Out of scope by `AGENTS.md`: plaintext config passwords, non-expiring session tokens, untested `DiscountCalculator`.
- The refusal log has no read path and no size bound (decision L33).
- The Playwright suite was re-run after Polish (17 passed), but its over-limit UI test checks status and approval count only, not `Version` or every decision field. No Playwright run happened during the review passes. The first-signature role and the threshold derivation are deferred design trade-offs.
- `README.md` was stale after the role change (it listed `demo.approver` and the Approver role). It now lists `demo.manager`, `demo.director`, `demo.director2` and `demo.admin` as in `appsettings.json`, says Admin is administrative only, and summarises the approval limits. Documentation only; the change is part of this branch once committed.
- `ai-logs/ai-interactions.jsonl` in the working tree has newer unredacted hook lines; only the redacted snapshot is committed.

## 12. Demo video

> **Placeholder.** Required length: **8-10 minutes**. Link: `TODO`. Not recorded yet.

## 13. Evidence checklist

| Item | Location | Status |
|---|---|---|
| Specification and checklist | `specs/001-role-approval-limits/` | committed |
| Playwright run, 17 passed (HEAD `9169ff3`, 2026-11-01) | `e2e/tests/refusals.spec.ts:106` and the other 16 specs; the console output is not saved in the repo (`e2e/playwright-report` is git-ignored) | re-runnable; result recorded in this file only **[uncertain]** |
| Tasks T001-T048 | `specs/001-role-approval-limits/tasks.md` | committed |
| Red-run and break-rule logs | `docs/testing/` | committed (Phase A raw output not saved) |
| Mutation baseline and final | `docs/mutation/` | committed; `after-ai-tests.txt` intentionally absent |
| AI interaction log | `ai-logs/ai-interactions.jsonl` | committed, redacted snapshot |
| Human decisions | `ai-logs/human-decisions.jsonl` | committed (L31-39 back-filled) |
| Context experiment | `ai-logs/context-experiment/` | committed |
| Flow diagram | `docs/diagrams/approval-flow.md` | committed (`9169ff3`) |
| Review verdicts F1-F10 | `docs/reviews/review-findings.md` | committed with this write-up |
| Reviewer evaluation | `/tmp/pr-eval*` | **not in repo** |
| Evaluation scoring | chat | **not in log** |
| Demo video (8-10 minutes) | link | placeholder, not recorded |

## 14. How to reproduce

```
dotnet test backend/InvoiceApp.sln
cd e2e && npm install && npx playwright install chromium && npm test
cd backend/src/InvoiceApp.Domain && dotnet stryker --test-project ../../tests/InvoiceApp.Tests/InvoiceApp.Tests.csproj
```
