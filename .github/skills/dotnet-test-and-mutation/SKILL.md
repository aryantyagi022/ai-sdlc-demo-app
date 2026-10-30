---
name: dotnet-test-and-mutation
description: How to run, scope and trust the xUnit and Stryker suites in this repo, including the mandatory step of breaking a rule to confirm the right test fails. Use when adding or changing tests, when asked whether something is covered, or before claiming a change is verified.
---

# Testing and mutation testing

## Commands

```bash
# Everything.
dotnet test backend/InvoiceApp.sln

# One class.
dotnet test backend/InvoiceApp.sln --filter FullyQualifiedName~ApprovalServiceTests

# One test.
dotnet test backend/InvoiceApp.sln --filter FullyQualifiedName~Approve_RefusesADirectorApprovingTheSameInvoiceTwice

# Mutation testing. The version is pinned; the guard rejects an unpinned install.
dotnet tool install -g dotnet-stryker --version 4.13.0
cd backend/src/InvoiceApp.Domain && dotnet stryker
```

Baseline: `docs/mutation/before.txt`. Domain started at 30.77% overall, `ApprovalService.cs` at
60.00% with 8 survivors (10 survived across the whole Domain project).

## The rule: a green suite proves nothing

A passing test tells you the test ran. It does not tell you the test would notice if the rule
were removed. **A rule is not covered until you have seen the right test fail for the right
reason.**

So, for every rule you add or change:

1. Make the suite green.
2. **Break the rule in the production code** — comment the check out, flip the comparison,
   short-circuit it with `false &&`.
3. Run the suite and read *which* tests failed.
4. Restore the code and confirm green again.
5. **Report which tests failed.** "All tests pass" is not a verification statement; "removing the
   distinctness check fails exactly 4.3, 4.4 and 5.3" is.

Expect a *narrow, specific* failure. If breaking one rule fails thirty tests, your tests are
coupled to something other than the rule. If it fails **none**, you have no coverage at all —
which is the situation this step exists to find.

Worked examples from this repo, all real:

| Break | Should fail |
|---|---|
| Remove the duplicate-approver check | only 4.3, 4.4, 5.3 |
| Widen rule 4 to a cross-invoice history | only 4.10, 4.11 |
| Mutate the invoice before validating | all of 5.1–5.5 |
| Replace refusal messages with "Not allowed." | the four e2e specs asserting a reason |

## The repository trap

`FakeInvoiceRepository.Update` used to be a no-op. Every test passed, and three
`_repository.Update(invoice)` → `;` mutants survived, because **no test could tell persistence
apart from silence**.

The fake now clones on read and does a version compare-and-swap, matching
`InMemoryInvoiceRepository`. The discipline that follows:

> A test that changes an invoice must **re-read it through the repository** and assert on the
> re-read copy. Asserting on the in-memory object you just mutated proves nothing.

Any new fake repository must keep clone-on-read and the CAS. A convenient no-op fake silently
removes a whole class of coverage.

## Reading a Stryker report honestly

- **Scope matters more than the headline number.** `ApprovalService.cs` is the file under change;
  that is the score to quote.
- `DiscountCalculator.cs` sits at 0% with 17 no-coverage mutants. It is pre-existing and out of
  scope. Report it as a known gap — never quietly fold it into the result, and never pad the
  overall score by testing it instead of the thing you changed.
- A **surviving mutant on a rule path means the test matrix is wrong**, not that the mutant is
  acceptable. Fix the test.
- Some mutants are genuinely equivalent. Say which, and why, rather than silently ignoring them.

Mutants that only die if you write the test deliberately:

| Mutant | Killed only by |
|---|---|
| `.Any(...)` → `.All(...)` | a test approving with an **empty** approvals list — with one existing approval the two agree |
| `<=` → `<` | the exact-boundary tests, 50,000.00 and 500,000.00 |
| refusal message → `""` | asserting the message **names the limit or role**, not just the code |
| `_repository.Update(invoice)` → `;` | approve, then **re-read**, then assert |

## Writing tests here

- One test class per class under test, named `<ClassUnderTest>Tests`.
- Tag tests with the UC-3 rule number they prove. If a test traces to no spec line, either the
  test or the spec is wrong.
- API-level tests use `WebApplicationFactory<Program>`; give each test its **own** factory, since
  the in-memory store is shared and several tests change state.
- Never hardcode demo passwords. Resolve `IOptions<DemoUsersOptions>` from the test host instead.
