# Implementation plan — multi-level invoice approval (phased)

## The brief

1. A Manager can approve up to 50,000.
2. A Director can approve up to 500,000.
3. Above 500,000 needs two different approvers, and the second must be a Director.
4. **The same user may never count as both approvers on one invoice.**
5. **A refused attempt gives a clear reason and changes nothing on the invoice.**

Rules 4 and 5 are the ones flagged as highest risk, so phases 5 and 7 are built around proving them
rather than merely implementing them.

**Rule 4 is scoped to a single invoice.** It is *not* a global "this user has approved before"
restriction: one person may approve any number of different invoices, and may be the first approver on
one invoice and the second approver on another. What they may never do is supply both signatures on
*the same* invoice. Concretely, the check reads `invoice.Approvals` — never a cross-invoice history —
and phase 5 includes a test that exists purely to pin this (test 4.10).

## Decisions already confirmed

| # | Question | Decision |
|---|---|---|
| 1 | Role model | Existing approvers become **Manager**; add **Director**; **Admin** administrative only |
| 2 | Approver role | Retired — migrated into Manager |
| 3 | Partial state | `Approvals` collection + `FirstApprovedBy` + "needs second approval" indicator; stays `PendingApproval` |
| 4 | First signature above 500,000 | Any Manager or Director; only the second must be a Director |
| 5 | Rejection | Any Manager or Director, any amount, alone; prior approvals kept |
| 6 | Self-approval | Both rules: submitter cannot approve **and** the same user cannot supply both signatures on the same invoice |
| 7 | Limits | **Inclusive**, configured per role in `appsettings.json` |
| 8 | Refusals | Reason codes; **403** authority, **409** lifecycle |
| 9 | Demo data | `demo.manager`, `demo.director`, `demo.director2`, `demo.admin` + two large invoices |

---

# Phase 1 — Context

Establish the baseline so later phases have something to measure against.

**Files changed:** none (read-only).

**Work:** confirm the exploration map still holds; record the starting numbers — 7 tests passing,
Stryker overall **30.77 %**, `ApprovalService.cs` **60.00 %** with 8 survivors
(`docs/mutation/before.txt`). Note that four of those survivors are directly on the code this change
touches: `_repository.Update(invoice)` → `;` at lines 27, 39 and 57, and `EnsurePending(invoice)` → `;`
at line 51. They survive because `FakeInvoiceRepository.Update` is a no-op
(`FakeInvoiceRepository.cs:15`), so no test can tell persistence apart from silence.

**How I'll know it worked:** `dotnet test backend/InvoiceApp.sln` reports 7 passing, and the four
mutants above are named as explicit targets for phase 7. Nothing is "known" by assertion — the numbers
are copied from a run, not from this document.

---

# Phase 2 — Skills and agents

Set up the AI tooling the later phases depend on, and make sure the repo's own guards are respected.

**Files changed:** none expected; `ai-logs/.state/stage` advances as stages progress (hook-managed, not
hand-edited).

**Work:**
- Stage tracking: the logging hooks read `Stage:` from each prompt (`.github/hooks/README.md`); the
  tracked stage currently reads `setup`, and will move through `planning` → `implementation` →
  `review`.
- Agents used, and deliberately not used: the **explore** agent produced the context map in phase 1;
  **rubber-duck** reviews the two-approver rule design at the end of phase 5; **code-review** runs over
  the full diff in phase 8. Implementation phases are done directly — they are single continuous traces
  across a handful of known files, so delegating them would cost context without buying isolation.
- Guards to work with, not around (`.github/hooks/scripts/guard.py`): commits must be
  `type(scope): summary` plus a `Why:` line; Stryker must be installed pinned
  (`dotnet tool install dotnet-stryker --version 4.13.0`); no `--force` pushes, no `rm -rf`, no
  credentials in diffs — which matters in phase 6, where `appsettings.json` demo passwords are touched.
- The `ai-interaction-log` skill writes exactly **one** task-level entry, in phase 9.

**How I'll know it worked:** a deliberate dry-run of the Stryker install command is accepted by the
guard (pinned version), and `tail -1 ai-logs/ai-interactions.jsonl | jq .` is readable at the end of
phase 9 with `action_verification` naming a real command and result.

---

# Phase 3 — Spec

Write the rules down as a testable specification before any production code, so phases 4 and 5 are
implementing against a fixed target rather than discovering rules as they go.

**Files changed:**
- `docs/use-cases/UC-3-multi-level-approval.md` *(new)* — follows the house style of `UC-1`/`UC-2`:
  background, the change, what the business asked for, out of scope, open questions. This is the
  written form of the nine decisions above.

**Work:** state the authority ladder, the derived dual threshold, the ordered validation sequence, the
five refusal reason codes, and the two worried-about rules in explicit, assertable language — e.g.
"a user who has already approved **this invoice** may not approve it again; approvals on other
invoices place no restriction on them", and "an approval attempt that is refused leaves `Status`, `Approvals`, `FirstApprovedBy`, `DecidedBy`,
`DecidedAt`, `DecisionNote` and `Version` byte-identical".

**How I'll know it worked:** every row in the phase 5 and 7 test matrices traces to a numbered line in
UC-3, and no test exists that UC-3 does not call for. If a test has no spec line, one of the two is
wrong.

---

# Phase 4 — Single-approver limits

Rules 1 and 2: the role→limit ladder, plus the model and plumbing everything else needs.

**Files changed:**

| File | Change |
|---|---|
| `backend/src/InvoiceApp.Domain/Invoice.cs` | Add `SubmittedBy`, `Version`, `Approvals`, `FirstApprovedBy`, computed `RequiresSecondApproval` |
| `backend/src/InvoiceApp.Domain/ApprovalRecord.cs` *(new)* | `record ApprovalRecord(string Username, string Role, DateTime ApprovedAt)` |
| `backend/src/InvoiceApp.Domain/Approver.cs` *(new)* | `record Approver(string Username, string Role)` |
| `backend/src/InvoiceApp.Domain/ApprovalPolicy.cs` *(new)* | Role→limit map; `DualApprovalThreshold` derived as the highest limit; `SecondApproverRole` |
| `backend/src/InvoiceApp.Domain/ApprovalRefusal.cs` *(new)* | `ApprovalRefusalReason` enum + `ApprovalRefusedException` |
| `backend/src/InvoiceApp.Domain/ApprovalService.cs` | `Submit(id, submittedBy)`; `Approve(id, Approver)` with ordered validation and the solo-limit path |
| `backend/src/InvoiceApp.Domain/IInvoiceRepository.cs` | `Update` returns `bool` (version CAS); reads return clones |
| `backend/src/InvoiceApp.Api/InMemoryInvoiceRepository.cs` | Implement clone-on-read + CAS under the existing lock |
| `backend/src/InvoiceApp.Api/Controllers/ApiControllerBase.cs` | Map `ApprovalRefusedException` → **403** with `{ error, reason }` |
| `backend/src/InvoiceApp.Api/Controllers/ApprovalsController.cs` | Build `Approver` from the session; `[RequireRole("Manager","Director")]` |
| `backend/src/InvoiceApp.Api/Controllers/InvoicesController.cs` | `Submit` passes the session username |
| `backend/src/InvoiceApp.Api/Program.cs` | Bind and register `ApprovalPolicy` from `Approval:RoleLimits` |
| `backend/src/InvoiceApp.Api/appsettings.json` | `Approval:RoleLimits`; replace users with the four demo accounts |
| `backend/tests/InvoiceApp.Tests/FakeInvoiceRepository.cs` | **Replace the no-op `Update`** with clone + CAS |
| `backend/tests/InvoiceApp.Tests/ApprovalServiceTests.cs` | Boundary and refusal tests below |

**Tests:** boundaries are inclusive, so each limit gets both sides — Manager at 50,000.00 approves, at
50,000.01 is refused `AboveApprovalLimit`; Director at 500,000.00 approves, at 500,000.01 is refused
(no solo path). A Manager on 200,000 is refused: the 50k–500k band has no dual path. Admin is refused
`RoleCannotApprove`. Submitter approving their own invoice is refused `SelfApprovalNotAllowed`.

**How I'll know it worked:** `dotnet test backend/InvoiceApp.sln` green with roughly 20 tests; a test
that approves and then re-reads through the repository now **fails** if `Update` is removed — which is
the first real signal, because it is exactly the mutant that survived at baseline.

---

# Phase 5 — Two-approver rule (rules 3, 4 and 5)

The phase the brief is most worried about. Implementation is small; the test matrix is the deliverable.

**Files changed:** `ApprovalService.cs` (dual path + distinctness), `ApprovalPolicy.cs` (threshold
derivation), `ApprovalServiceTests.cs` (the matrix below). No new files.

**Behaviour:** above the threshold, the first signature may come from any Manager or Director — their
own limit governs *solo* authority, not participation — the invoice stays `PendingApproval` with
`FirstApprovedBy` set; the second signature must come from a **Director** who is **not** already in
*this invoice's* `Approvals` and is **not** the submitter; only then does it become `Approved`.

### Test matrix — rule 4, "the same user may never be both approvers on one invoice"

The distinctness check is `invoice.Approvals.Any(a => a.Username == approver.Username)` — it reads
**only this invoice's** approvals. There is no cross-invoice approver history, and none is to be
introduced: approving invoice A must never restrict the same person on invoice B. Test 4.10 is the
guard against that misreading creeping in later.

| # | Scenario | Expected |
|---|---|---|
| 4.1 | `demo.manager` first, `demo.director` second, 750,000 | `Approved`, 2 approvals |
| 4.2 | `demo.director` first, `demo.director2` second, 750,000 | `Approved`, 2 approvals |
| 4.3 | `demo.director` approves **the same invoice** twice | 403 `DuplicateApprover`, still 1 approval |
| 4.4 | `demo.manager` first, then `demo.manager` again on that invoice | 403 `DuplicateApprover` |
| 4.5 | `demo.manager` first, `demo.manager2` (also Manager) second | 403 `SecondApproverMustBeDirector` — distinct, but wrong role |
| 4.6 | Submitter is `demo.director`; they try the second signature | 403 `SelfApprovalNotAllowed` — checked **before** distinctness, so the reason is the more specific one |
| 4.7 | First approval on an **empty** approvals list | Succeeds — pins `Any` rather than `All` (see phase 7) |
| 4.8 | Username comparison is `Ordinal`: `Demo.Director` ≠ `demo.director` | Treated as a different user; asserted explicitly so the choice is deliberate, not accidental |
| 4.9 | Two Directors race on the second signature (concurrent `Update`) | Exactly one wins; the loser gets 409 from the version CAS, never a third approval |
| 4.10 | **Scope:** `demo.director` approves invoice A, then approves a *different* invoice B | **Both succeed.** The restriction is per-invoice; prior approvals elsewhere are irrelevant |
| 4.11 | **Scope:** `demo.director` is second approver on A, then *first* approver on B | **Both succeed** — no role-sequence memory across invoices |

### Test matrix — rule 5, "a refused attempt changes nothing"

Each refusal case snapshots the invoice before the attempt and asserts **every** field afterwards:
`Status`, `Approvals` (count and contents), `FirstApprovedBy`, `DecidedBy`, `DecidedAt`,
`DecisionNote`, **and `Version`**. Version is the sharpest assertion — it fails if the service wrote at
all, even if it wrote the same values back.

| # | Refusal | Reason code | HTTP |
|---|---|---|---|
| 5.1 | Over your solo limit | `AboveApprovalLimit` | 403 |
| 5.2 | Your own submission | `SelfApprovalNotAllowed` | 403 |
| 5.3 | You already approved | `DuplicateApprover` | 403 |
| 5.4 | Second approver is not a Director | `SecondApproverMustBeDirector` | 403 |
| 5.5 | Role cannot approve (Admin) | `RoleCannotApprove` | 403 |
| 5.6 | Invoice is already `Approved`/`Rejected`/`Draft` | — lifecycle | 409 |
| 5.7 | Invoice does not exist | — | 404 |
| 5.8 | Reject with a blank reason | — | 400 |

Plus: **5.9** every refusal message names the concrete fact behind it (the limit, or the required
role) — not just the code, because phase 7 shows that message-only assertions are the difference
between killing and not killing the string mutants; and **5.10** rejection at any amount by a Manager
on a 900,000 invoice succeeds with prior approvals **preserved**, so "changes nothing" is not
over-applied to the one path that legitimately keeps history.

**How I'll know it worked:** all ~26 new tests green; deleting the distinctness check makes 4.3–4.4
fail; widening it from `invoice.Approvals` to any cross-invoice lookup makes **4.10–4.11** fail; moving
any mutation above a validation check makes the whole of 5.1–5.5 fail. The suite must fail loudly for
the right reason — I'll confirm by temporarily breaking each rule, not by assuming.

---

# Phase 6 — UI and e2e

**Files changed:**

| File | Change |
|---|---|
| `backend/src/InvoiceApp.Api/wwwroot/index.html` | Submitter column; approvals/"needs a Director" area |
| `backend/src/InvoiceApp.Api/wwwroot/app.js` | Role-gate approve/reject; "1 of 2 — needs a Director" badge; disable with a reason for own submission, own prior approval, over-limit; render reason codes as readable sentences |
| `backend/src/InvoiceApp.Api/InMemoryInvoiceRepository.cs` | Seed ~750,000 `PendingApproval`, ~900,000 with a first approval from `demo.manager`, and a third ~600,000 `PendingApproval` so the per-invoice scope spec has a second large invoice to approve |
| `e2e/pages/LoginPage.ts`, `InvoicesPage.ts`, `ApprovalsPage.ts` *(new)* | Page objects, `getByRole`/`getByLabel` only |
| `e2e/tests/approval-limits.spec.ts`, `two-approver.spec.ts`, `refusals.spec.ts` *(new)* | Specs below |
| `backend/tests/InvoiceApp.Tests/ApprovalsControllerTests.cs`, `InvoicesControllerTests.cs` *(new)* | API-level 401/403+reason/409/200 |
| `backend/tests/InvoiceApp.Tests/InvoiceApp.Tests.csproj` | Add `Microsoft.AspNetCore.Mvc.Testing` + project reference to `InvoiceApp.Api` |
| `backend/src/InvoiceApp.Api/Program.cs` | `public partial class Program { }` for `WebApplicationFactory` |

**E2E specs:** solo approval by `demo.manager` within limit; the two-signature flow `demo.manager` →
`demo.director` on 900,000, asserting the badge reads "1 of 2" in between; `demo.director` attempting
both signatures **on one invoice** and being refused with a visible reason; the same
`demo.director` then approving a **different** large invoice successfully in the same session, which
is the user-visible proof that the restriction is per-invoice; an over-limit refusal showing the
limit; and `demo.admin` seeing no approve/reject controls at all.

**How I'll know it worked:** `cd e2e && npm install && npx playwright install chromium && npm test` is
green, and the refusal specs assert on **visible text the user would read**, not on status codes — the
only check that the reason actually reaches a human. The duplicate-approver spec is the end-to-end
proof of rule 4.

---

# Phase 7 — Mutation

Prove the phase 5 tests are load-bearing rather than merely present. This is the real verification for
the two worried-about rules — a green suite only shows the tests ran, not that they would catch a
regression.

**Files changed:** `docs/mutation/after.txt` *(new)*; `stryker-config.json` *(new, if a config file is
needed to scope the run)*. No production code changes unless a surviving mutant exposes a genuine gap —
in which case the fix lands in `ApprovalService.cs` or the tests, and is called out.

**Command** (pinned, as the guard requires):
`dotnet tool install dotnet-stryker --version 4.13.0` then run against
`backend/src/InvoiceApp.Domain`.

**Named targets, carried from the baseline:**

| Baseline survivor | Killed by |
|---|---|
| `_repository.Update(invoice)` → `;` at lines 27, 39, 57 | The real `FakeInvoiceRepository.Update` plus approve-then-re-read assertions (phase 4) |
| `EnsurePending(invoice)` → `;` at line 51 | Test 5.6 — rejecting an already-decided invoice |
| New: `a.Username == approver.Username` → `!=` | Tests 4.1/4.2 (different user succeeds) and 4.3 (same user refused) together |
| New: `Approvals.Any(...)` → `.All(...)` | Test **4.7** specifically — with one existing approval `Any` and `All` agree, so only the empty-list case separates them |
| New: distinctness widened beyond this invoice | Tests **4.10–4.11** — not a Stryker mutant but the same discipline: the only tests that fail if someone "helpfully" turns the per-invoice check into a cross-invoice approver history |
| New: `amount <= limit` → `<` / `>` | The inclusive-boundary pairs at 50,000.00 / 50,000.01 and 500,000.00 / 500,000.01 |
| New: refusal-message string mutants | Test 5.9 — asserting the message names the limit or role |

**Targets:** `ApprovalService.cs` from 60.00 % to **≥ 90 %**, with **zero survivors on the distinctness
check and the refusal paths**. Overall score will also rise, but is a weaker signal.

**Explicitly out of scope:** `DiscountCalculator.cs` sits at 0 % with 17 no-coverage mutants. It is
pre-existing, untouched by this change, and chasing it would inflate the headline number without
testing anything in the brief. It stays a known gap and is reported as one, not quietly folded into the
result.

**How I'll know it worked:** `docs/mutation/after.txt` shows the four named baseline survivors as
**killed**, and no survivor remains on the distinctness or refusal code paths. If a mutant survives
there, the test matrix is wrong and phase 5 reopens — that is the point of running this.

---

# Phase 8 — Review

**Files changed:** whatever review turns up; expected to be small.

**Work:** `code-review` agent over the full diff; `rubber-duck` specifically on the dual-approval
design — the question worth asking is whether a Manager being able to *start* a chain they cannot
*finish* is genuinely intended, since it lets a large invoice sit at "1 of 2" indefinitely with no
escalation or expiry in scope. Re-check the exploration report's risk list: which risks this change
closes (self-approval, lost updates, client-side-only enforcement, no-op `Update`, untested rules) and
which remain open and deliberately unaddressed (plaintext passwords, non-expiring tokens,
constant-time compare, no global exception handler).

**How I'll know it worked:** `dotnet test` and the E2E suite still green after any review fixes, and
every review finding is either fixed or recorded with a reason — not silently dropped.

---

# Phase 9 — Write-up

**Files changed:**
- `docs/plan/PLAN-multi-level-approval.md` *(new)* — this plan, saved on approval
- `README.md` — the role ladder, the four demo accounts, the dual-approval rule
- `AGENTS.md` — convention updates: approval rules live in `ApprovalService`, refusals carry reason
  codes, `[RequireRole]` usage for the new roles
- `docs/mutation/after.txt` — committed alongside `before.txt` so the pair tells the story
- `ai-logs/ai-interactions.jsonl` — **one** entry via the `ai-interaction-log` skill

**The log entry:** `--stage implementation`, `--request` close to the original brief,
`--verification` naming the real commands and results (test counts, mutation score before → after),
`--limitation` recording what was left undone — `DiscountCalculator` still at 0 %, auth hardening out
of scope, no escalation for stuck invoices — and `--human-decision` only if quoting the user verbatim.

**How I'll know it worked:** `tail -1 ai-logs/ai-interactions.jsonl | jq .` shows one well-formed
record whose `action_verification` names commands actually run, and `README.md` sign-in instructions
work against the running app — checked by signing in, not by reading the file.

---

## Phase order and gates

`1 → 2 → 3 → 4 → 5 → 6 → 7 → 8 → 9`, with two hard gates: **phase 5 does not close until its matrix is
green**, and **phase 7 can send phase 5 back**. Phase 6 depends on phase 4's API shape, so the UI is
not started before the reason codes are fixed.

## Open risks

- A Manager can start a chain they cannot finish above 500,000; no escalation or expiry in scope.
- The 50k–500k band has no dual path — two Managers cannot combine; a Director is required.
- Role rename is breaking: `demo.approver` stops working.
- Clone-on-read changes behaviour: code that forgets `Update` silently stops persisting. Phase 4's
  re-read tests are the guard.
- Reason codes become API surface once the UI switches on them.
- `DiscountCalculator.cs` remains at 0 % mutation score, untouched and out of scope.
