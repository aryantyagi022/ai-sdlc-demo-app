# Tasks: Approval Limits by Role

**Input**: `specs/001-role-approval-limits/` (spec.md, plan.md, research.md, data-model.md, contracts/approvals-api.md, quickstart.md)

**Phases**: A = single-approver limits, B = two-approver rule, C = UI and e2e. Phases 1 and 2 are shared groundwork. Tests come first in every phase.

**Format**: `- [ ] T### [P?] [Story?] Description with file path`. `[P]` = different files, no dependency on an open task.

**Paths**: `API` = `backend/src/InvoiceApp.Api`, `DOM` = `backend/src/InvoiceApp.Domain`, `TST` = `backend/tests/InvoiceApp.Tests`.

**Stories** (spec.md): US1 approve within role limit, US2 two approvers above 500,000, US3 refusals explain themselves and change nothing, US4 same person cannot sign twice.

## Test kinds (read before running anything)

Most of this feature already exists, so most tests cannot honestly start red. Every test task is tagged:

- **[NEW]** new behaviour. Must fail by an **assertion failure** against the current code or a stub, before its implementation task. A compile error or `NotImplementedException` is **not** evidence and must not be recorded as "failed for the intended reason".
- **[EXISTING]** pins behaviour the code already has. It is expected to pass on first run. It only counts once the matching break-the-rule step has made it fail on purpose and it has been restored.

Commit messages record which tests were seen failing by assertion and which break-the-rule steps were run.

## Phase 1: Setup (test support and shared types)

- [X] T001 [P] Create `DOM/IRefusalLog.cs` (`void Record(RefusalLogEntry entry)`) and `DOM/RefusalLogEntry.cs` with fields `InvoiceId, Username, Role, Action (Approve|Reject), Reason, Message, OccurredAt` (UTC); no repository dependency
- [X] T002 [P] Add record `ApprovalRefusal(ApprovalRefusalReason Reason, string Message)` to `DOM/ApprovalRefusal.cs`
- [X] T003 [P] Add `UpdateCount` (number of **successful** `Update` saves) to `TST/FakeInvoiceRepository.cs`
- [X] T004 Create `TST/FakeRefusalLog.cs` implementing `IRefusalLog` from T001 and exposing the recorded `RefusalLogEntry` list
- [X] T005 Create `TST/InvoiceAssert.cs`: `Capture(repository, invoiceId)` returns a snapshot of `Status`, `Approvals`, `FirstApprovedBy`, `DecidedBy`, `DecidedAt`, `DecisionNote`, `Version` **and the repository's current `UpdateCount`**, called immediately before the refused operation; `AssertUnchanged(repository, snapshot)` re-reads the invoice, compares every captured field, and asserts `repository.UpdateCount` equals the **captured baseline** (not zero, because setup may already have saved)

## Phase 2: Foundational (blocks all phases)

- [X] T006 [P] Create `API/InMemoryRefusalLog.cs` (thread-safe list) and register it as a singleton in `API/Program.cs` (wiring only)
- [X] T007 Inject `IRefusalLog` into `ApprovalService` in `DOM/ApprovalService.cs` (no use yet); update the service construction in `TST/ApprovalServiceTests.cs` and `TST/ApprovalsControllerTests.cs` to pass a `FakeRefusalLog`
- [X] T008 Run `dotnet test backend/InvoiceApp.sln`; the existing suite must be green before any behaviour changes

**Checkpoint**: groundwork compiles; behaviour unchanged.

## Phase 3: Phase A, single-approver limits (US1, US3, US4 basics) 🎯 MVP

**Goal**: role limits, the role gate, the full order of evaluation (spec FR-015), reject rules, self-approval and duplicate refusals, refusal log, "a refusal changes nothing".

**Independent test**: Manager approves 50,000.00 but not 50,000.01; Director approves 500,000.00; Admin is refused for approve and reject without learning whether the invoice exists; a submitter cannot approve their own invoice; nobody signs the same invoice twice.

**Scope note**: `ApprovalPolicy.RefuseApproval` is moved in one step, including the above-threshold branch, as a behaviour-preserving refactor so the existing suite stays green. Phase B then proves that branch rather than writing it.

### Tests first

- [X] T009 [P] [US1] [EXISTING] `TST/ApprovalPolicyTests.cs`: `RefuseApproval` at 50,000.00 (Manager allowed), 50,000.01 (Manager `AboveApprovalLimit`; message states the amount and the 50,000.00 limit), 50,000.01 (Director allowed), 500,000.00 (Director allowed); role names matched with different casing (`"manager"`, `"MANAGER"`)
- [X] T010 [P] [US1] [EXISTING] `TST/ApprovalPolicyTests.cs`: `RefuseRole` returns `RoleCannotApprove` for Admin and for an unknown role; message names the role
- [X] T011 [P] [US1] [EXISTING] `TST/ApprovalPolicyTests.cs`: no two-approver path between 50,000.01 and 500,000: a Manager on an invoice already signed by another Manager is still `AboveApprovalLimit`
- [X] T012 [P] [US3] [EXISTING] `TST/ApprovalPolicyTests.cs` and `TST/ApprovalServiceTests.cs`: **plain self-approval refusal**: the submitter of a 1,000.00 invoice approving it gets `SelfApprovalNotAllowed` (message names the user and invoice) with nothing else true; a submitter who has also already signed gets `SelfApprovalNotAllowed`, not `DuplicateApprover`; `AssertUnchanged` holds
- [X] T013 [P] [US4] [EXISTING] `TST/ApprovalPolicyTests.cs` and `TST/ApprovalServiceTests.cs`: duplicate approver: the same user signing the same invoice twice gets `DuplicateApprover` (message names the user and invoice) even when the amount is also above their limit; the same user may approve a different invoice; usernames `"Alice"`, `"alice"` and `"alice "` are three different people (ordinal, no trim); `AssertUnchanged` holds
- [X] T014 [P] [US3] [NEW for reject order, EXISTING for approve order] `TST/ApprovalServiceTests.cs`: order of evaluation per FR-015: Admin approve on an unknown id → `RoleCannotApprove` (not `KeyNotFoundException`); Admin reject with blank reason → `RoleCannotApprove`; Director reject of an unknown id with blank reason → `KeyNotFoundException`; Director reject of a non-pending invoice with blank reason → `InvalidOperationException`; Director reject of a pending invoice with blank reason → `ArgumentException`; the self-approval and duplicate checks are not applied to rejection
- [X] T015 [P] [US3] [EXISTING] `TST/ApprovalServiceTests.cs`: every Phase A approval refusal (T009–T013) leaves the invoice untouched using `InvoiceAssert.Capture` before and `AssertUnchanged` after
- [X] T016 [P] [US3] [EXISTING] `TST/ApprovalServiceTests.cs`: reject by Manager and by Director at any amount stores reason, `DecidedBy`, `DecidedAt`, keeps existing `Approvals`; the submitter rejecting their own invoice succeeds; Admin reject refused
- [X] T017 [P] [US3] [NEW] `TST/ApprovalServiceTests.cs`: each of the five coded refusals writes exactly one `RefusalLogEntry` (invoice id as supplied, exact username, role, action, reason, the same message, UTC timestamp); 404, 409 and 400 write none; the invoice and `UpdateCount` are unaffected by logging
- [X] T018 [P] [US3] [NEW for the body cases, EXISTING otherwise] `TST/ApprovalsControllerTests.cs`: Admin reject with **no body** and with `{}` → 403 `RoleCannotApprove`; Manager reject with no reason on a pending invoice → 400; Manager approve at 50,000.01 → 403 `AboveApprovalLimit` with the amount and 50,000.00 in `error`
- [X] T019 [P] [US3] [EXISTING] `TST/ApprovalsControllerTests.cs`: **HTTP 404 and 409**: Director approve and Director reject (with a reason) on an unknown invoice id → 404; Director approve and Director reject on the already-approved `INV-1003` → 409; after each, read the invoice through the `IInvoiceRepository` from `_application.Services` and assert status, approvals, first approver, decided-by, decided-at, decision note and `Version` are unchanged
- [X] T020 [P] [US1] [EXISTING] `TST/ApprovalsControllerTests.cs`: **configuration binding**: using `WithWebHostBuilder` and `UseSetting` (not `ConfigureAppConfiguration`: `Program.cs` reads `Approval:*` straight after `CreateBuilder`, before those callbacks apply, so the extra role was never seen and the test got 403), add role `Clerk` with limit 1,000 under `Approval:RoleLimits` and a `Clerk` demo user; that user approves `INV-1002` (980.50) → 200 and is refused 403 `AboveApprovalLimit` on `INV-1001` (4,250.00); without the extra configuration the same user is refused `RoleCannotApprove`; no controller change is needed
- [X] T021 [US3] Add signature-only stubs `RefuseRole` and `RefuseApproval` to `DOM/ApprovalPolicy.cs` that return `null` (no refusal), so tests compile and fail on assertions rather than on missing members
- [X] T022 [US3] Run the Phase A tests and classify the result: **[NEW]** tests (T014 reject order, T017 refusal log, T018 body cases) must fail by assertion, and the failure text is recorded; policy tests (T009–T013) fail by assertion against the null stubs, which only shows the stub is empty; **[EXISTING]** HTTP and service tests (T015, T016, T019, T020 and the approve parts of T014) are expected to pass. Record any compile or `NotImplementedException` failure as "not evidence" and fix the setup, not the claim

### Implementation

- [X] T023 [US1] Implement `ApprovalPolicy.RefuseRole` and `RefuseApproval` in `DOM/ApprovalPolicy.cs` as pure methods (no I/O, no clock, no exceptions, no logging): self, duplicate, then single-approver limit, and the above-threshold branch moved unchanged; message text moved from `ApprovalService`
- [X] T024 [US1] Rewrite `ApprovalService.Approve` in `DOM/ApprovalService.cs` to: `RefuseRole` → `Find` → `EnsurePending` → `RefuseApproval` → record signature → `Save`; throw `ApprovalRefusedException` from a refusal and write the `IRefusalLog` entry before rethrowing
- [X] T025 [US3] Rewrite `ApprovalService.Reject` to: `RefuseRole` (log it) → `Find` → `EnsurePending` → blank reason check (`ArgumentException`) → decide → `Save`; no self or duplicate check
- [X] T026 [US1] Point `ApprovalService.EligibilityFor` at the same policy methods in the same order (drop the try/catch round-trip)
- [X] T027 [US3] `API/Controllers/ApprovalsController.cs`: `RejectRequest(string? Reason)` and `[FromBody] RejectRequest? request` using `request?.Reason`; no other logic
- [X] T028 Run `dotnet test backend/InvoiceApp.sln`; all Phase A tests green
- [X] T029 [US3] Break-the-rule, one at a time, restoring after each, recording which test failed: `<=` → `<` in the limit comparison (expect T009); drop the role gate (T010, T014); remove the self check (T012); remove the duplicate check (T013); swap self and duplicate (T012); move the reason check above `Find` (T014); drop the log write (T017); call `Update` before throwing a refusal (T015); remove the nullable reason (T018). Any rule with no failing test gets a new test before moving on

**Checkpoint**: single-approver ladder and refusal contract complete on the API.

**Deferred limitation (Phase A review)**: `InMemoryRefusalLog` keeps every entry for the life of the process and nothing reads or trims them, so it grows without bound. Accepted for this in-memory demo, where the spec requires recording and separation from the invoice, not retention or querying. Not redesigned here; a durable or bounded log with a read path would be needed to close it. `SecondApproverMustBeDirector` no-change coverage (`InvoiceAssert` with `UpdateCount`) belongs to Phase B (T030 onward).

## Phase 4: Phase B, two-approver rule (US2, US4)

**Goal**: above 500,000 two different people sign, the second a Director (the configured second-approver role, default Director); one person never fills both slots on one invoice.

**Independent test**: Manager signs 500,000.01, invoice stays pending; a different Director signs, it is approved; the same user signing twice is refused.

The above-threshold branch exists since T023, so these tests are **[EXISTING]** and rely on break-the-rule. Any that fails by assertion on first run is a real defect and gets fixed first.

### Tests first

- [X] T030 [P] [US2] [EXISTING] `TST/ApprovalPolicyTests.cs`: at 500,000.00 one Director signature suffices; at 500,000.01 the first signature is allowed for a Manager and for a Director; a second signature by a Manager → `SecondApproverMustBeDirector` (message states the amount and the Director role); a second signature by a different Director is allowed; Director first then a different Director second is allowed
- [X] T031 [P] [US4] [EXISTING] `TST/ApprovalPolicyTests.cs`: precedence on a large invoice with several refusals true at once: Admin → `RoleCannotApprove`; the submitter who is also a Director who already signed → `SelfApprovalNotAllowed`; a Manager who already signed as first approver → `DuplicateApprover`, not `SecondApproverMustBeDirector`
- [X] T032 [P] [US2] [EXISTING] `TST/ApprovalServiceTests.cs`: full flow on a 600,000 invoice: first signature leaves `PendingApproval` with one approval and `FirstApprovedBy` set; the second Director signature sets `Approved`, `DecidedBy`, `DecidedAt`; each refused step leaves the invoice unchanged via `InvoiceAssert`
- [X] T033 [P] [US2] [EXISTING] (the stale-read wrapper already existed as a `file class` in `ApprovalServiceTests.cs` and was kept there; added a test asserting no change, `UpdateCount` 1 and no refusal-log entry) `TST/StaleReadRepository.cs` and `TST/ApprovalServiceTests.cs`: a repository wrapper serves a stale first read of the invoice (it does not bump the stored `Version`), so `Update` sees a newer stored version; the service throws `InvalidOperationException` (409) and the stored invoice holds only the winner's change
- [X] T034 [P] [US2] [EXISTING] (a Manager signs first so the race is between two second signatures; a test-only repository wrapper around the real `InMemoryInvoiceRepository` holds both Directors' `GetById` at a `Barrier(2)` so both read the same version before either saves, with no sleeps) `TST/ApprovalsControllerTests.cs`: two Directors race on the same 600,000 invoice; exactly one 200 and one 409 (from the real version check), final status `Approved`, two signatures (the Manager's first and one Director's second) and `Version` 2; which Director wins is not asserted
- [X] T035 Run the Phase B tests. Passing is expected. If any fails by assertion, that is a defect: fix it in `DOM/ApprovalPolicy.cs` or `DOM/ApprovalService.cs` (completion needs 2 signatures above the threshold, 1 otherwise), then re-run
- [X] T036 Run `dotnet test backend/InvoiceApp.sln`; all green
- [X] T037 [US4] Break-the-rule, one at a time, restoring after each, recording which test failed: second-approver check accepts any role (T030); remove the shared duplicate-approver check (T031); results saved in `docs/testing/phase-b-break-rules.txt`; invert the first-signature branch so a Manager cannot sign first (T030); complete after one signature above the threshold (T032); make `Update` ignore the version (T033, T034). Add a test for any rule with no failing test

**Checkpoint**: full approval ladder complete and proven on the API.

## Phase 5: Phase C, UI and e2e (US1–US4 end to end)

**Goal**: a person sees the ladder working and the refusal message. The UI reads eligibility from the server and never re-derives the rules (spec US3 scenario 3). Do not redesign the existing UI.

**Independent test**: `cd e2e && npm test` passes and the rendered page shows the refusal messages.

### Tests first

- [X] T038 [P] [US1] [EXISTING] (already present from `b2eb3c9`; kept) `e2e/tests/approval-limits.spec.ts`: Manager sees Approve disabled on INV-1006 with the 50,000.00 limit in its explanation; Director approves INV-1006; role-based locators only (`getByRole`, `getByLabel`), through `e2e/pages/ApprovalsPage.ts` (the specs create their own invoices of the same amounts instead of mutating shared seed rows)
- [X] T039 [P] [US2] [EXISTING] (already present; kept) `e2e/tests/two-approver.spec.ts`: a Manager signs first and it stays pending with "1 of 2 - needs a Director", then a Director completes it; two Directors can sign first and second; a Manager cannot give the second signature (Approve disabled, explanation names Director); one Director alone cannot approve a large invoice. The same-user duplicate message is covered in `refusals.spec.ts`, not here. Specs create their own invoices so shared server state cannot leak between them
- [X] T040 [P] [US3] [NEW only if it fails] `e2e/tests/refusals.spec.ts` (the Admin "cannot approve or reject" note and absent controls are covered by the existing `approval-limits.spec.ts`, not here): a rejection with a blank reason displays the server's message and leaves the row unchanged (existing); a refusal displays the exact message returned by the server, amount and limit for the over-limit case and user and invoice for the duplicate case (added; the other refusal codes are asserted only partly at e2e level and fully in the domain tests) (added four specs: an over-limit approval the page wrongly offered, via a rewritten invoice list, is refused by the server and its message shown; a second window of the same Director gets the duplicate message; an earlier error clears after a success; submitter and signer history are visible; plus `submittedByFor`/`approvedByFor` in `ApprovalsPage.ts`)
- [X] T041 Run `cd e2e && npm test`. Record exactly which assertions fail and why. A failure is "intended" only if it is an assertion about visible behaviour, not a locator typo or timeout (13 existing specs green before the additions. Of the four new specs, two failed: the submitter/history spec failed by assertion because the Approvals tab did not show them, which is intended; the over-limit spec failed on my own wrong expectation of "None" where the page shows "0 of 1", so the test was corrected and no UI change was made for it)

### Implementation (conditional)

- [X] T042 [US3] **Only if T041 shows a failing acceptance assertion**: make the smallest change in `API/wwwroot/app.js` or `API/wwwroot/index.html` that turns that assertion green (show the server `error` text, clear stale errors, disable controls from `eligibility`); no role or amount logic in the page. If T041 is all green, make **no** UI change and record that in the commit message (one change, for the failing submitter/history assertion: each pending card shows "Submitted by …" and "Approved by … (Role)" from the server's invoice data, plus a `.history` rule in `styles.css`; error text, stale-error clearing and eligibility-driven controls already passed and were not touched)
- [X] T043 [US3] Open the rendered page (Playwright MCP `browser_snapshot`, `browser_take_screenshot`) as Manager, Director and Admin and check what no assertion covers: message wording, disabled-button explanations, layout, no stale error text. Report anything found; change the UI only if it breaks an acceptance assertion (done with a Playwright script and screenshots, not the MCP tools; nothing found that needed a change)
- [X] T044 Run `cd e2e && npm test`; all green (17 passed; break-the-rule results in `docs/testing/phase-c-break-rules.txt`)

## Phase 6: Polish and cross-cutting

- [X] T045 [P] Update the "Approval rules live in `ApprovalService`" sentence in `AGENTS.md` to say the rules live in `ApprovalPolicy` and the service orchestrates (done; also corrected the Stryker command in `AGENTS.md` and `quickstart.md`, because the bare command finds no test project)
- [X] T046 Run mutation testing `cd backend/src/InvoiceApp.Domain && dotnet stryker`; the score must not fall below `docs/mutation/before.txt`; add tests for surviving mutants in `ApprovalPolicy` (needs `--test-project ../../tests/InvoiceApp.Tests/InvoiceApp.Tests.csproj`; overall 30.77% to 72.66%, `ApprovalService` 60.00% to 83.33%, `ApprovalPolicy` 82.14% to 94.64% after one added test; results and survivor verdicts in `docs/mutation/after.txt`)
- [X] T047 Run `dotnet test backend/InvoiceApp.sln` and `cd e2e && npm test`; walk the `quickstart.md` manual table rows 1–9 (from a fresh clone of `b723c62`: 135 dotnet tests and 17 e2e specs passed; rows 1–9 walked with curl on a fresh server and all matched, with `Version` unchanged after each 403)
- [X] T048 Self-review the diff with the `pr-self-review` skill (every finding fixed, dismissed or deferred with a reason) and write the `ai-interaction-log` entry. That AI interaction log is separate from the application's refusal log (adversarial pass by a `rubber-duck` agent, 5 findings: 3 deferred design findings below, the `IsSameUser` survivor and the stale-task note dismissed (see "Polish review verdicts"); the new `ApprovalPolicy` test was break-verified in an isolated copy, see `docs/mutation/after.txt`; one task-level log entry written and one corrective entry appended that supersedes the stale limitation in the first; `human-decisions.jsonl` back-filled with verbatim wording only, reconstructions labelled)

### Polish review verdicts

Each finding from the adversarial review has exactly one verdict.

| # | Finding | Verdict | Reason | What future work would need |
|---|---|---|---|---|
| 1 | Any role in `Approval:RoleLimits` may give the first signature above the dual-approval threshold (`ApprovalPolicy.cs:117`), so adding a low-limit role such as a Clerk also lets it participate in a high-value approval | **deferred** | Confirmed decision: "a role limit governs solo authority, not participation", and only Manager and Director exist today; no configuration can trigger it yet | An explicit configured list of roles eligible to sign first above the threshold, validated at startup, plus a test with a low-limit role on a high-value invoice |
| 2 | The dual-approval threshold is the highest configured limit (`ApprovalPolicy.cs:51`), so adding a role with a higher limit moves the point at which two signatures are needed | **deferred** | Intended by the current design (the ladder's top is the threshold) and entrenched by an existing test; with the demo ladder it equals the specified 500,000 | Either an explicit `Approval:DualApprovalThreshold` setting, or startup validation that the second-approver role owns the maximum limit, plus configuration-change tests |
| 3 | The UI hard-codes "needs a Director" (`wwwroot/app.js:31-32`) although `Approval:SecondApproverRole` is configurable | **deferred** | Correct for the shipped configuration; changing the setting is not yet a supported scenario | Project the required second-approver role in the server's invoice view and render that value, with a test and an e2e case for a non-Director configuration |
| 4 | `IsSameUser` `&&` → `\|\|` survivor was called not equivalent | **dismissed** | Equivalent by argument (see `docs/mutation/after.txt`); this is a reasoned conclusion, not an experimentally proven one | A test or a proof tool would be needed to upgrade "by argument" to "proven" |
| 5 | `after.txt` claimed completion while T045–T048 were unchecked | **dismissed** | Stale once the tasks were completed and the evidence updated | none |

## Dependencies and order

```text
Phase 1 (T001-T005) -> Phase 2 (T006-T008) -> Phase A (T009-T029) -> Phase B (T030-T037) -> Phase C (T038-T044) -> Polish (T045-T048)
```

- T004 needs T001; T005 needs T003; T006 and T007 need T001; T021 needs nothing but must precede T022.
- Within a phase: tests, run and classify, implement, run, break-the-rule.
- Phase B follows A because it exercises the policy built in T023. Phase C needs the API from A and B.

## Parallel examples

- Phase 1: T001, T002, T003 together, then T004, T005.
- Phase A tests: T009–T020 (T009–T013 share `ApprovalPolicyTests.cs`, so write them in one pass; T014–T017 share `ApprovalServiceTests.cs`; T018–T020 share `ApprovalsControllerTests.cs`).
- Phase B tests: T030–T034 across their three files.
- Phase C tests: T038, T039, T040 (separate spec files).

## Independent test criteria

| Story | Passes when |
|---|---|
| US1 | 50,000.00 and 500,000.00 approve alone; 50,000.01 refused for Manager; no two-Manager path; extra configured role works |
| US2 | 500,000.01 needs two signatures, the second a Director; Director plus a different Director completes |
| US3 | every coded refusal states its concrete facts (SC-004), leaves the invoice unchanged, and logs one entry; 404, 409 and 400 behave per FR-013 |
| US4 | the same user cannot sign twice on one invoice, can sign others; usernames compare ordinally |

## Implementation strategy

- **MVP**: Phases 1, 2 and A: the single-approver ladder and the whole refusal contract.
- Then B, then C. One commit per task or logical pair, in `type(scope): summary` + `Why:` format.
- Out of scope: UC-1 bulk approval, UC-2 partial approval, a log-reading endpoint or UI, persistent log storage.
