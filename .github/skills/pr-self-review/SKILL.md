---
name: pr-self-review
description: How to review your own diff before asking anyone else to, including a required adversarial pass that argues against your own design and the fixed fixed/dismissed/deferred verdicts. Use before opening a PR, before claiming work is finished, or when asked to review a change.
---

# Reviewing your own change

Reviewing your own work is mostly an exercise in not believing yourself. The checklist is the
easy half; the adversarial pass is the half that finds things.

## 1. Read the diff as a whole

```bash
git --no-pager diff main...HEAD --stat
git --no-pager diff main...HEAD
```

Look for what you did not mean to do: debug output, commented-out code, a file touched for no
reason, a temporary workaround that became permanent.

## 2. The checklist

From AGENTS.md, in order of how expensive it is to get wrong:

- A refused approval leaves `Status`, `Approvals`, `DecidedBy`, `DecidedAt`, `DecisionNote` and
  `Version` untouched.
- The duplicate-approver check reads **only the current invoice**.
- Inclusive boundaries are tested on **both** sides.
- Business rules are in Domain — not a controller, not `Program.cs`.
- Role restrictions are enforced server-side, not just by hiding a button.
- Role eligibility is not duplicated outside `Approval:RoleLimits`.
- New rules have tests that fail when the rule is removed — **confirmed by removing it**.

## 3. The adversarial pass — required

Before you write the summary, argue against yourself. The useful findings come from here.

Delegate it. Ask a `rubber-duck` agent to attack the design, and **give it the case for your
chosen approach** so it has something to push against. Do not ask it whether your change looks
fine; ask it to find the strongest argument that you are wrong.

Questions that have actually paid off on this codebase:

- Is this abstraction earning its keep, or is it evaluating the same condition twice?
- Have I applied the fix **everywhere** it applies? (A role check was hoisted in `Approve` and
  not in `Reject`. The duck caught it; the tests did not.)
- What does this break on the next change — adding a role, a limit, a status?
- What did I quietly decide that the user never agreed to?
- What fails at startup, or worse, does not fail at startup when it should?

**If the duck wins, change the design.** On this repo it overturned a recommended authorisation
approach outright and raised a blocking gap — missing fail-fast configuration validation — that
the checklist would never have surfaced. A self-review that never changes your mind was theatre.

## 4. Re-read the original risks

If the work started from an exploration or a plan with a risk list, go back to it and say, risk
by risk, which this change **closes** and which remain **open**. Risks that are quietly dropped
look identical to risks that were handled.

## 5. Verdicts

Every finding gets exactly one, and **all three are reported**:

- **fixed** — changed in this diff.
- **dismissed** — not a problem, *with the reason*.
- **deferred** — real, but out of scope, *with what it would take*.

A dismissed finding with a reason is worth more than a silent one. A finding with no verdict is a
finding you are hoping nobody notices.

Known and deliberately out of scope — do **not** re-report these as new, only if your change
makes them worse: plaintext passwords in configuration, session tokens that never expire,
`DiscountCalculator` having no tests.

## 6. Say how you know

The summary states what was verified and how, with real numbers from real runs:

> `dotnet test backend/InvoiceApp.sln` — 67 passed. `cd e2e && npm test` — 13 passed. Disabling
> the per-invoice distinctness check fails only the rule 4 spec.

Not "tests pass". Not "should work". If something was not verified, **say that too** — an honest
gap is cheaper than a confident claim that turns out to be wrong.

## Commit and PR

Format is enforced by `.github/hooks/scripts/guard.py`:

```
type(scope): summary

Why: the reason this change exists.
```

The PR body carries the same thing at more length: what changed, why, how it was verified, and
the three verdict lists.
