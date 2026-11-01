# Invoice App Constitution

## Core Principles

### I. Rules Live Only in Domain
Every business rule lives in `InvoiceApp.Domain`. Controllers, `Program.cs`, the repository and the
UI hold none. Role limits are owned by `ApprovalPolicy`, bound from `Approval:RoleLimits`; no second
copy of that fact may exist elsewhere.

### II. Every Rule Has a Positive and a Negative Test
A rule is not done until one test shows it permitting what it should and another shows it refusing
what it should. Boundaries are tested on both sides. A test only counts once the rule has been
broken on purpose and that test failed.

### III. A Refusal Changes Nothing
Validate everything before mutating anything. A refused attempt leaves `Status`, `Approvals`,
`DecidedBy`, `DecidedAt`, `DecisionNote` and `Version` untouched, and the refusal names the concrete
fact (the limit or the required role) so a person can act on it.

### IV. Controllers Stay Thin
A controller binds the request, calls one service method through `Handle(...)`, and returns the
result. It decides nothing. `[RequireRole]` is only for static permissions that configuration does
not own, such as `Delete`.

### V. Commits Are Small
One reason per commit, in the form `type(scope): summary` with a `Why:` line. A commit that needs
"and" to describe it is two commits.

### VI. The Mutation Score Never Drops
The Domain mutation score must not fall below the recorded baseline. A change that lowers it is not
mergeable until tests restore it.

### VII. No Real Credentials
No real credentials, tokens or customer data in the repository, logs or fixtures. Demo accounts
are fake by definition.

## Constraints

- Stack: ASP.NET Core 8 Web API, xUnit, Stryker, Playwright.
- Known and out of scope: plaintext demo passwords in configuration, non-expiring session tokens.
  Do not re-report them unless a change makes them worse.

## Workflow

- Specifications come before code; each rule in a spec is numbered and testable.
- Reviews give every finding one verdict: fixed, dismissed or deferred, with a reason.
- Runtime guidance for agents lives in `AGENTS.md` and `.github/skills/`.

## Governance

This constitution supersedes other practices. Amendments are made in a commit that states why, and
bump the version below. Reviews verify compliance with each principle. Complexity that departs from
a principle must be justified in writing.

**Version**: 1.0.0 | **Ratified**: 2026-10-31 | **Last Amended**: 2026-10-31
